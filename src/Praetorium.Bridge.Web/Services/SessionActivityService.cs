using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Praetorium.Bridge.Agents;
using Praetorium.Bridge.Hooks;
using Praetorium.Bridge.Sessions;
using Praetorium.Bridge.Signaling;

namespace Praetorium.Bridge.Web.Services;

/// <summary>
/// Source of a <see cref="SessionTranscriptEntry"/>.
/// </summary>
public enum SessionTranscriptSource
{
    /// <summary>Bridge-level lifecycle event (spawn, pool, drop, response, input request/received, crash).</summary>
    Lifecycle,

    /// <summary>A signal posted through <see cref="ISignalRegistry"/> (outbound or inbound).</summary>
    Signaling,

    /// <summary>An internal SDK event surfaced by <see cref="IAgentSessionObservable"/> — assistant text, tool start/complete, error, idle.</summary>
    Agent,
}

/// <summary>
/// A single entry in a session's live transcript as displayed by the Sessions dashboard.
/// Flat shape so heterogeneous sources render uniformly.
/// </summary>
public sealed record SessionTranscriptEntry(
    DateTimeOffset Timestamp,
    string SessionId,
    SessionTranscriptSource Source,
    string Kind,
    string? Title,
    string? Details);

/// <summary>
/// Per-session live transcript buffer for the Sessions dashboard view. Subscribes
/// to bridge-level activity, signaling events, and — once an agent session is
/// spawned — its <see cref="IAgentSessionObservable"/> stream when available.
/// </summary>
public sealed class SessionActivityService : IDisposable
{
    private const int MaxEntriesPerSession = 500;

    private readonly ConcurrentDictionary<string, List<SessionTranscriptEntry>> _buffers = new();
    private readonly ConcurrentDictionary<string, Action<AgentActivityEvent>> _agentHandlers = new();

    private readonly DashboardBridgeHooks _hooks;
    private readonly ISignalRegistry _signals;
    private readonly ISessionManager _sessions;
    private readonly ILogger<SessionActivityService> _logger;

    /// <summary>
    /// Raised when a new transcript entry has been appended. Consumers (the Blazor
    /// page) should marshal onto the UI thread before mutating component state.
    /// </summary>
    public event Action<SessionTranscriptEntry>? EntryAdded;

    /// <summary>
    /// Raised when the set of known sessions may have changed (spawn, drop, crash).
    /// </summary>
    public event Action? SessionsChanged;

    public SessionActivityService(
        DashboardBridgeHooks hooks,
        ISignalRegistry signals,
        ISessionManager sessions,
        ILogger<SessionActivityService> logger)
    {
        _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
        _signals = signals ?? throw new ArgumentNullException(nameof(signals));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _hooks.SessionLifecycle += OnSessionLifecycle;
        _hooks.SessionSpawned += OnSessionSpawnedAsync;
        _signals.Signaled += OnSignaled;
    }

    /// <summary>
    /// Returns the current list of session IDs known to have transcript data.
    /// </summary>
    public IReadOnlyList<string> GetKnownSessionIds() => _buffers.Keys.ToList();

    /// <summary>
    /// Returns a snapshot of the transcript buffer for the given session, newest-last.
    /// </summary>
    public IReadOnlyList<SessionTranscriptEntry> GetTranscript(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            return Array.Empty<SessionTranscriptEntry>();

        if (!_buffers.TryGetValue(sessionId, out var list))
            return Array.Empty<SessionTranscriptEntry>();

        lock (list)
        {
            return list.ToList();
        }
    }

    /// <summary>
    /// Returns the current set of sessions managed by the bridge. Used by the
    /// dashboard left-hand list.
    /// </summary>
    public Task<IReadOnlyList<SessionInfo>> GetSessionsAsync(CancellationToken ct) =>
        _sessions.GetAllSessionsAsync(ct);

    /// <summary>
    /// Cancels any in-flight signal waits for the session by issuing a Reset on
    /// both channels. Used by the dashboard's "Cancel" button.
    /// </summary>
    public Task<bool> CancelWaitsAsync(string sessionId, CancellationToken ct) =>
        _sessions.CancelSessionWaitsAsync(sessionId, ct);

    private void OnSessionLifecycle(SessionLifecycleEntry entry)
    {
        var transcript = new SessionTranscriptEntry(
            entry.Timestamp,
            entry.SessionId,
            SessionTranscriptSource.Lifecycle,
            entry.Kind,
            entry.ToolName,
            entry.Details);

        Append(transcript);

        if (entry.Kind is "Spawned" or "Dropped" or "Crashed" or "Pooled" or "Woken")
        {
            SafeInvokeSessionsChanged();
        }
    }

    private Task OnSessionSpawnedAsync(SessionContext context)
    {
        // The spawn hook runs after the bridge's own spawn logic, so the agent
        // session is retrievable via the session manager at this point.
        var agent = _sessions.GetActiveAgent(context.SessionId);
        if (agent is IAgentSessionObservable observable)
        {
            AttachAgentObserver(context.SessionId, observable);
        }

        return Task.CompletedTask;
    }

    private void AttachAgentObserver(string sessionId, IAgentSessionObservable observable)
    {
        // If a previous observer was attached for this sessionId, let it go — the
        // old agent instance is disposed anyway, so its event will never fire again.
        Action<AgentActivityEvent> handler = evt => OnAgentActivity(sessionId, evt);
        if (_agentHandlers.TryAdd(sessionId, handler))
        {
            observable.ActivityRaised += handler;
        }
    }

    private void OnAgentActivity(string sessionId, AgentActivityEvent evt)
    {
        var title = evt.Kind switch
        {
            AgentActivityKind.AssistantMessage => "assistant",
            AgentActivityKind.ToolStart => evt.ToolName ?? "tool",
            AgentActivityKind.ToolComplete => evt.ToolName ?? "tool",
            AgentActivityKind.Error => "error",
            AgentActivityKind.Idle => "idle",
            _ => evt.Kind.ToString(),
        };

        var details = evt.Kind switch
        {
            AgentActivityKind.ToolStart => evt.ArgumentsJson,
            AgentActivityKind.ToolComplete when evt.Success == false => evt.Content ?? "failed",
            AgentActivityKind.ToolComplete => evt.Success.HasValue ? $"success={evt.Success}" : null,
            _ => evt.Content,
        };

        var transcript = new SessionTranscriptEntry(
            evt.Timestamp,
            sessionId,
            SessionTranscriptSource.Agent,
            evt.Kind.ToString(),
            title,
            details);

        Append(transcript);
    }

    private void OnSignaled(SignalingEvent evt)
    {
        var title = $"{evt.Direction} / {evt.Type}";
        var details = SerializeSignalData(evt.Data);

        var transcript = new SessionTranscriptEntry(
            evt.Timestamp,
            evt.SessionId,
            SessionTranscriptSource.Signaling,
            evt.Type.ToString(),
            title,
            details);

        Append(transcript);
    }

    private static readonly JsonSerializerOptions SignalSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string? SerializeSignalData(object? data)
    {
        if (data == null) return null;
        if (data is string s) return s;
        try
        {
            return JsonSerializer.Serialize(data, SignalSerializerOptions);
        }
        catch
        {
            return data.ToString();
        }
    }

    private void Append(SessionTranscriptEntry entry)
    {
        var list = _buffers.GetOrAdd(entry.SessionId, _ => new List<SessionTranscriptEntry>());
        lock (list)
        {
            list.Add(entry);
            if (list.Count > MaxEntriesPerSession)
            {
                list.RemoveRange(0, list.Count - MaxEntriesPerSession);
            }
        }

        try { EntryAdded?.Invoke(entry); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SessionActivityService EntryAdded subscriber threw.");
        }
    }

    private void SafeInvokeSessionsChanged()
    {
        try { SessionsChanged?.Invoke(); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SessionActivityService SessionsChanged subscriber threw.");
        }
    }

    public void Dispose()
    {
        _hooks.SessionLifecycle -= OnSessionLifecycle;
        _hooks.SessionSpawned -= OnSessionSpawnedAsync;
        _signals.Signaled -= OnSignaled;
    }
}
