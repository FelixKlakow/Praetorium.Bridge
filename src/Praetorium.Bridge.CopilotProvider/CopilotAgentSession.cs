using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using GitHub.Copilot.SDK;
using Praetorium.Bridge.Agents;
using Praetorium.Bridge.CopilotProvider.InternalMcp;

namespace Praetorium.Bridge.CopilotProvider;

/// <summary>
/// Wraps a <see cref="CopilotSession"/> into the provider-agnostic <see cref="IAgentSession"/> interface.
/// Also implements <see cref="IAgentSessionObservable"/> so the dashboard can render a live
/// transcript of the agent's internal activity (assistant text, tool calls, errors).
/// </summary>
internal sealed class CopilotAgentSession : IAgentSession, IAgentSessionObservable, IDisposable
{
    private readonly CopilotSession _session;
    private readonly IDisposable? _subscription;
    private readonly IInternalMcpRegistry _internalMcpRegistry;
    private readonly string _internalSessionKey;

    // ToolCallId → ToolName. Populated on ToolExecutionStartEvent, drained on
    // ToolExecutionCompleteEvent so the dashboard can label completion events
    // (the SDK's ToolExecutionCompleteData does not carry the name itself).
    private readonly ConcurrentDictionary<string, string> _pendingToolNames = new();

    // Completes the currently in-flight SendAsync call. CopilotSession.SendAsync
    // returns as soon as the message is queued (it returns the message id), so
    // we must wait for SessionIdleEvent to know the agent has actually finished
    // its turn. SessionErrorEvent faults the task.
    private TaskCompletionSource<string>? _turnCompletion;
    private string? _lastAssistantMessage;
    private readonly object _turnLock = new();

    /// <inheritdoc/>
    public event Action<AgentActivityEvent>? ActivityRaised;

    internal CopilotAgentSession(CopilotSession session, IInternalMcpRegistry internalMcpRegistry, string internalSessionKey)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _internalMcpRegistry = internalMcpRegistry ?? throw new ArgumentNullException(nameof(internalMcpRegistry));
        _internalSessionKey = internalSessionKey ?? throw new ArgumentNullException(nameof(internalSessionKey));
        _subscription = _session.On(OnSessionEvent);
    }

    /// <inheritdoc/>
    public async Task<string> SendAsync(string message, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(message))
            throw new ArgumentException("Message cannot be null or empty.", nameof(message));

        // Install the turn-completion TCS BEFORE queuing the message so that a
        // SessionIdleEvent fired synchronously off the RPC response is captured.
        TaskCompletionSource<string> tcs;
        lock (_turnLock)
        {
            if (_turnCompletion != null && !_turnCompletion.Task.IsCompleted)
                throw new InvalidOperationException(
                    "A previous SendAsync has not completed yet for this session.");

            tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _turnCompletion = tcs;
        }

        string messageId;
        try
        {
            messageId = await _session.SendAsync(new MessageOptions { Prompt = message }, ct)
                .ConfigureAwait(false);
        }
        catch
        {
            lock (_turnLock) { _turnCompletion = null; }
            throw;
        }

        // Reset the last-message buffer for this turn.
        lock (_turnLock) { _lastAssistantMessage = null; }

        using (ct.Register(() => tcs.TrySetCanceled(ct)))
        {
            // The TCS is completed by OnSessionEvent when SessionIdleEvent arrives
            // (or faulted on SessionErrorEvent). Until then the agent may run any
            // number of tools, including our signaling tools.
            return await tcs.Task.ConfigureAwait(false);
        }
    }

    private void OnSessionEvent(SessionEvent evt)
    {
        // Complete / fault the in-flight turn based on terminal session events.
        switch (evt)
        {
            case SessionIdleEvent:
                string? lastMsg;
                lock (_turnLock) { lastMsg = _lastAssistantMessage; }
                CompleteTurn(r => r.TrySetResult(lastMsg ?? string.Empty));
                break;
            case SessionErrorEvent errEvt:
                var msg = errEvt.Data?.Message ?? "session error";
                CompleteTurn(r => r.TrySetException(new InvalidOperationException("Session error: " + msg)));
                break;
        }

        // Track the last assistant message within the current turn.
        if (evt is AssistantMessageEvent assistantEvt && !string.IsNullOrEmpty(assistantEvt.Data?.Content))
        {
            lock (_turnLock) { _lastAssistantMessage = assistantEvt.Data!.Content; }
        }

        var handler = ActivityRaised;
        if (handler == null) return;

        AgentActivityEvent? activity = evt switch
        {
            AssistantMessageEvent msg when !string.IsNullOrEmpty(msg.Data?.Content) =>
                new AgentActivityEvent(
                    DateTimeOffset.UtcNow,
                    AgentActivityKind.AssistantMessage,
                    Content: msg.Data!.Content),

            ToolExecutionStartEvent start =>
                RecordStart(start),

            ToolExecutionCompleteEvent complete =>
                new AgentActivityEvent(
                    DateTimeOffset.UtcNow,
                    AgentActivityKind.ToolComplete,
                    ToolName: ResolveCompletedToolName(complete.Data?.ToolCallId),
                    Content: complete.Data?.Error?.Message,
                    Success: complete.Data?.Success),

            SessionErrorEvent err =>
                new AgentActivityEvent(
                    DateTimeOffset.UtcNow,
                    AgentActivityKind.Error,
                    Content: err.Data?.Message ?? "session error"),

            SessionIdleEvent =>
                new AgentActivityEvent(
                    DateTimeOffset.UtcNow,
                    AgentActivityKind.Idle),

            _ => null,
        };

        if (activity == null) return;

        try { handler(activity); }
        catch { /* observers must not break the agent session */ }
    }

    private AgentActivityEvent RecordStart(ToolExecutionStartEvent start)
    {
        var name = start.Data?.ToolName ?? "tool";
        var id = start.Data?.ToolCallId;
        if (!string.IsNullOrEmpty(id))
        {
            _pendingToolNames[id] = name;
        }
        return new AgentActivityEvent(
            DateTimeOffset.UtcNow,
            AgentActivityKind.ToolStart,
            ToolName: name,
            ArgumentsJson: start.Data?.Arguments?.ToString());
    }

    private string? ResolveCompletedToolName(string? toolCallId)
    {
        if (string.IsNullOrEmpty(toolCallId)) return null;
        return _pendingToolNames.TryRemove(toolCallId, out var name) ? name : null;
    }

    private void CompleteTurn(Action<TaskCompletionSource<string>> completion)
    {
        TaskCompletionSource<string>? tcs;
        lock (_turnLock)
        {
            tcs = _turnCompletion;
            _turnCompletion = null;
        }
        if (tcs != null)
            completion(tcs);
    }

    public void Dispose()
    {
        try { _subscription?.Dispose(); } catch { }
        CompleteTurn(r => r.TrySetCanceled());
    }
}
