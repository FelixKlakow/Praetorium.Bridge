using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using Praetorium.Bridge.Agents;
using Praetorium.Bridge.Configuration;
using Praetorium.Bridge.Hooks;
using Praetorium.Bridge.Mcp;
using Praetorium.Bridge.Prompts;
using Praetorium.Bridge.Sessions;
using Praetorium.Bridge.Signaling;

namespace Praetorium.Bridge.Tools;

/// <summary>
/// Main orchestrator for dispatching tool calls to AI agents.
/// Handles session management, parameter binding, prompt resolution, and signal handling.
/// Uses IAgentSession abstraction for provider-agnostic agent management.
/// </summary>
public class ToolDispatcher : IToolDispatcher
{
    /// <summary>
    /// Default interval for keepalive progress notifications emitted to the external caller
    /// while the agent is working. Used when the tool's signaling configuration does not
    /// specify an explicit keepalive interval.
    /// </summary>
    private static readonly TimeSpan DefaultKeepaliveInterval = TimeSpan.FromSeconds(15);

    private readonly IConfigurationProvider _configurationProvider;
    private readonly ISessionManager _sessionManager;
    private readonly ISignalRegistry _signalRegistry;
    private readonly IPromptResolver _promptResolver;
    private readonly IAgentProvider _agentProvider;
    private readonly IBridgeHooks _hooks;
    private readonly ILogger<ToolDispatcher> _logger;
    private readonly ToolParameterBinder _binder = new();

    /// <summary>
    /// Initializes a new instance of the ToolDispatcher class.
    /// </summary>
    public ToolDispatcher(
        IConfigurationProvider configurationProvider,
        ISessionManager sessionManager,
        ISignalRegistry signalRegistry,
        IPromptResolver promptResolver,
        IAgentProvider agentProvider,
        IBridgeHooks hooks,
        ILogger<ToolDispatcher> logger)
    {
        _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _signalRegistry = signalRegistry ?? throw new ArgumentNullException(nameof(signalRegistry));
        _promptResolver = promptResolver ?? throw new ArgumentNullException(nameof(promptResolver));
        _agentProvider = agentProvider ?? throw new ArgumentNullException(nameof(agentProvider));
        _hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ToolResponse> DispatchAsync(
        string toolName,
        JsonElement arguments,
        string? connectionId,
        IProgress<ProgressNotificationValue>? progress,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("Dispatching tool call: {ToolName}", toolName);

            // Step 1: Look up tool definition
            var config = _configurationProvider.Configuration;
            if (!config.Tools.TryGetValue(toolName, out var toolDef))
            {
                var error = $"Tool '{toolName}' is not defined in the configuration.";
                _logger.LogError(error);
                return ToolResponse.Error(error);
            }

            // Step 2: Bind and validate parameters
            ToolCallContext toolContext;
            try
            {
                toolContext = _binder.Bind(toolDef, arguments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Parameter binding failed for tool '{ToolName}'", toolName);
                return ToolResponse.Error($"Parameter validation failed: {ex.Message}");
            }

            if (connectionId != null)
            {
                toolContext = new ToolCallContext(
                    toolContext.BoundParameters,
                    toolContext.ResetSession,
                    toolContext.Input,
                    toolContext.ReferenceId,
                    connectionId);
            }

            // Step 3: Determine session mode and get or create session
            var agentConfig = toolDef.Agent ?? config.Defaults?.Agent ?? throw new InvalidOperationException("No agent configuration available");
            var sessionConfig = toolDef.Session ?? config.Defaults?.Session ?? new SessionConfiguration();
            var signalingConfig = toolDef.Signaling ?? config.Defaults?.Signaling ?? new SignalingConfiguration();
            var sessionMode = sessionConfig.Mode;

            // Step 3a: Validate reasoning effort configuration
            var model = agentConfig.Model ?? "claude-sonnet-4.6";
            if (!string.IsNullOrEmpty(agentConfig.ReasoningEffort))
            {
                await ValidateReasoningEffortAsync(model, agentConfig.ReasoningEffort, toolName, ct);
            }

            var (sessionInfo, isNew) = await _sessionManager.GetOrCreateSessionAsync(
                toolName,
                toolContext.ReferenceId,
                connectionId,
                sessionMode,
                agentConfig,
                ct);

            _logger.LogInformation("Session {SessionId} obtained (new={IsNew}) for tool '{ToolName}'", sessionInfo.SessionId, isNew, toolName);

            if (connectionId != null)
            {
                _signalRegistry.RegisterConnectionBinding(sessionInfo.SessionId, connectionId);
            }

            // Step 4: Decide whether to start a fresh agent turn or continue draining
            // an already-running one. A running turn's outbound signals accumulate in the
            // registry's FIFO queue; subsequent dispatches simply drain that queue in
            // order via WaitOutboundAsync. A new inbound reply, if supplied, is posted on
            // the inbound channel so any parked blocking signaling tool can resume.
            var existingTurn = isNew ? null : _sessionManager.GetRunningTurn(sessionInfo.SessionId);
            var hasRunningTurn = existingTurn != null && !existingTurn.IsCompleted;

            if (hasRunningTurn)
            {
                _logger.LogInformation("Reusing running turn for session {SessionId}", sessionInfo.SessionId);
                _ = _sessionManager.GetActiveAgent(sessionInfo.SessionId)
                    ?? throw new InvalidOperationException($"Session {sessionInfo.SessionId} has no active agent session");

                if (toolContext.Input != null)
                {
                    _signalRegistry.SignalInbound(sessionInfo.SessionId, SignalResult.Input(toolContext.Input));
                }
            }
            else
            {
                // Step 5: Resolve prompt and start a fresh agent turn in the background.
                string prompt;
                try
                {
                    prompt = await _promptResolver.ResolveAsync(toolName, toolDef, toolContext.BoundParameters, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Prompt resolution failed for tool '{ToolName}'", toolName);
                    await _sessionManager.MarkCrashedAsync(sessionInfo.SessionId, ex, ct);
                    return ToolResponse.Error($"Prompt resolution failed: {ex.Message}");
                }

                // Step 6: Create AI agent if new session, then kick off SendAsync as a
                // background turn task owned by the session. We must NOT await it here
                // because the agent may park inside a blocking signaling tool, and that
                // tool needs the dispatcher to proceed to WaitOutboundAsync to drain the
                // outbound signal it just posted.
                IAgentSession agentSession;
                try
                {
                    if (isNew)
                    {
                        var toolSources = new List<AgentToolSource>();
                        if (agentConfig.Tools != null && agentConfig.Tools.Count > 0)
                        {
                            foreach (var toolSourceKey in agentConfig.Tools)
                            {
                                if (config.AgentToolSources.TryGetValue(toolSourceKey, out var toolSource))
                                {
                                    toolSources.Add(toolSource);
                                }
                            }
                        }

                        var promptDirectory = Path.Combine(_configurationProvider.ConfigDirectory, "prompts");
                        var signalingTools = SignalingToolFactory.CreateSignalingTools(
                            sessionInfo.SessionId,
                            signalingConfig,
                            _signalRegistry,
                            promptDirectory);

                        agentSession = await _sessionManager.CreateAgentAsync(
                            sessionInfo.SessionId,
                            toolName,
                            prompt,
                            agentConfig,
                            toolSources,
                            signalingTools,
                            ct);
                    }
                    else
                    {
                        agentSession = _sessionManager.GetActiveAgent(sessionInfo.SessionId)
                            ?? throw new InvalidOperationException($"Session {sessionInfo.SessionId} has no active agent session");
                    }

                    // Subscribe to the outbound signal counter BEFORE invoking SendAsync so
                    // that any signal the agent emits synchronously — including before the
                    // first await yields — is counted. Failing to do so causes a race in
                    // which a productive turn is misreported as "ended without signaling".
                    var tracker = BeginTrackTurn(sessionInfo.SessionId);

                    // For a brand-new session the prompt was already installed as the
                    // system message when CreateAgentAsync ran. The first SendAsync only
                    // needs to trigger the agent to start; sending the full prompt again
                    // would duplicate it in the conversation context.
                    var userMessage = isNew ? "Begin." : prompt;

                    Task<string> turnTask;
                    try
                    {
                        turnTask = agentSession.SendAsync(userMessage, ct);
                    }
                    catch
                    {
                        tracker.Detach(_signalRegistry);
                        throw;
                    }
                    CompleteTrackTurn(sessionInfo.SessionId, turnTask, tracker);

                    _logger.LogInformation("Prompt sent to session {SessionId}", sessionInfo.SessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send prompt to session {SessionId}", sessionInfo.SessionId);
                    await _sessionManager.MarkCrashedAsync(sessionInfo.SessionId, ex, ct);
                    return ToolResponse.Error($"Failed to initialize agent session: {ex.Message}");
                }
            }

            // Step 7: Hook for tool invoked
            var hookParams = toolContext.BoundParameters
                .ToDictionary(kv => kv.Key, kv => (object?)kv.Value.ToString());
            await _hooks.OnToolInvokedAsync(
                new ToolInvocationContext(
                    Guid.NewGuid().ToString(),
                    toolName,
                    hookParams,
                    connectionId ?? "unknown"),
                ct);

            // Step 8: Wait for the next outbound signal from the agent. External MCP
            // callers are always blocking, so we keep waiting while emitting periodic
            // keepalive progress notifications. The wait races the running turn so a
            // clean turn end without any outbound signal is reported as an error
            // instead of hanging forever.
            var responseTimeoutSeconds = sessionConfig.ResponseTimeoutSeconds > 0
                ? sessionConfig.ResponseTimeoutSeconds
                : 600;
            var timeout = TimeSpan.FromSeconds(responseTimeoutSeconds);
            var keepaliveInterval = signalingConfig.KeepaliveIntervalSeconds > 0
                ? TimeSpan.FromSeconds(signalingConfig.KeepaliveIntervalSeconds)
                : DefaultKeepaliveInterval;

            SignalResult signal;
            try
            {
                signal = await WaitForAgentSignalAsync(
                    sessionInfo.SessionId, timeout, keepaliveInterval, progress, ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Tool call cancelled for tool '{ToolName}', session {SessionId}", toolName, sessionInfo.SessionId);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error waiting for signal for session {SessionId}", sessionInfo.SessionId);
                await _sessionManager.MarkCrashedAsync(sessionInfo.SessionId, ex, ct);
                return ToolResponse.Error($"Error waiting for agent response: {ex.Message}");
            }

            // Step 9: Convert signal to response
            ToolResponse response = signal.Type switch
            {
                SignalType.Timeout => ToolResponse.Error($"Agent response timeout (>{timeout.TotalSeconds} seconds)"),
                SignalType.Disconnect => ToolResponse.Error("Connection was disconnected"),
                SignalType.Reset => ToolResponse.Error("Session was reset"),
                SignalType.Input => ConvertInputSignal(signal.Data),
                _ => ToolResponse.Error($"Unknown signal type: {signal.Type}")
            };

            // Step 10: Fire hooks
            var responseMessage = response.Message ?? response.ErrorMessage ?? "Tool executed";
            var responseDuration = sessionInfo.DurationMs;
            await _hooks.OnResponseDeliveredAsync(
                new ResponseContext(
                    Guid.NewGuid().ToString(),
                    sessionInfo.SessionId,
                    toolName,
                    responseMessage,
                    responseDuration),
                ct);

            _logger.LogInformation("Tool call completed for tool '{ToolName}', session {SessionId}, status={Status}",
                toolName, sessionInfo.SessionId, response.Status);

            return response;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in ToolDispatcher for tool '{ToolName}'", toolName);
            return ToolResponse.Error($"Unhandled error: {ex.Message}");
        }
    }

    /// <summary>
    /// Waits for the next outbound signal from the agent while emitting periodic keepalive
    /// progress notifications to the external caller. The signal registry queues outbound
    /// signals FIFO, so the dispatcher simply blocks here until either the agent posts the
    /// next message or a terminal error signal is injected by <see cref="TrackTurn"/>.
    /// </summary>
    private async Task<SignalResult> WaitForAgentSignalAsync(
        string sessionId,
        TimeSpan timeout,
        TimeSpan keepaliveInterval,
        IProgress<ProgressNotificationValue>? progress,
        CancellationToken ct)
    {
        if (progress == null)
        {
            return await _signalRegistry.WaitOutboundAsync(sessionId, timeout, ct).ConfigureAwait(false);
        }

        using var reporter = new ProgressReporter(progress, keepaliveInterval, "agent working");
        reporter.Start();
        try
        {
            return await _signalRegistry.WaitOutboundAsync(sessionId, timeout, ct).ConfigureAwait(false);
        }
        finally
        {
            reporter.Stop();
        }
    }

    /// <summary>
    /// Tracks outbound signals emitted during a single agent turn. A <see cref="TurnTracker"/>
    /// is obtained via <see cref="BeginTrackTurn"/> BEFORE the agent's SendAsync is invoked
    /// so that any signal emitted synchronously by the agent is counted. The tracker is then
    /// finalized by <see cref="CompleteTrackTurn"/> once the turn task has been created,
    /// which installs the continuation that releases the event subscription and injects a
    /// terminal error when the turn ends without any outbound signal.
    /// </summary>
    private sealed class TurnTracker
    {
        public required string SessionId { get; init; }
        public Action<SignalingEvent> Handler = _ => { };
        public int OutboundCount;
        public bool Detached;

        public void Detach(ISignalRegistry registry)
        {
            if (Detached) return;
            registry.Signaled -= Handler;
            Detached = true;
        }
    }

    /// <summary>
    /// Subscribes to the signal registry and returns a <see cref="TurnTracker"/>. Must be
    /// called BEFORE the agent's SendAsync to avoid a race where synchronous outbound
    /// signals are missed.
    /// </summary>
    private TurnTracker BeginTrackTurn(string sessionId)
    {
        var tracker = new TurnTracker { SessionId = sessionId };

        tracker.Handler = evt =>
        {
            if (string.Equals(evt.SessionId, sessionId, StringComparison.Ordinal)
                && evt.Direction == SignalingDirection.Outbound)
            {
                Interlocked.Increment(ref tracker.OutboundCount);
            }
        };

        _signalRegistry.Signaled += tracker.Handler;
        return tracker;
    }

    /// <summary>
    /// Registers the turn task with the session manager and attaches a completion handler.
    /// The handler pushes a terminal error signal onto the outbound channel when the turn
    /// faults, is cancelled, or ends cleanly without the agent having emitted any outbound
    /// signal \u2014 guaranteeing that a blocked external caller is always unblocked.
    /// </summary>
    private void CompleteTrackTurn(string sessionId, Task<string> turnTask, TurnTracker tracker)
    {
        _sessionManager.SetRunningTurn(sessionId, turnTask);

        _ = turnTask.ContinueWith(
            t =>
            {
                tracker.Detach(_signalRegistry);

                // Only clear the tracked turn if it is still this one; a subsequent
                // dispatch may have replaced it.
                if (ReferenceEquals(_sessionManager.GetRunningTurn(sessionId), turnTask))
                {
                    _sessionManager.ClearRunningTurn(sessionId);
                }

                try
                {
                    if (t.IsFaulted)
                    {
                        var ex = t.Exception?.GetBaseException();
                        _logger.LogError(ex, "Agent turn faulted for session {SessionId}", sessionId);
                        _signalRegistry.SignalOutbound(sessionId, SignalResult.Input(
                            ToolResponse.Error($"Agent turn failed: {ex?.Message ?? "unknown error"}")));
                    }
                    else if (t.IsCanceled)
                    {
                        _logger.LogWarning("Agent turn was cancelled for session {SessionId}", sessionId);
                        _signalRegistry.SignalOutbound(sessionId, SignalResult.Input(
                            ToolResponse.Error("Agent turn was cancelled.")));
                    }
                    else if (Volatile.Read(ref tracker.OutboundCount) == 0)
                    {
                        // The agent ended its turn without calling any signaling tool.
                        // If it produced a plain assistant message (t.Result is non-empty),
                        // surface that text as a complete response so the caller receives
                        // something useful rather than a generic error. This handles the
                        // case where a capable agent skips the signaling tool contract
                        // and just writes its answer as a chat message.
                        var assistantText = t.Result;
                        if (!string.IsNullOrWhiteSpace(assistantText))
                        {
                            _logger.LogWarning(
                                "Agent for session {SessionId} ended its turn without calling any signaling tool. " +
                                "Surfacing plain assistant message as response.",
                                sessionId);
                            _signalRegistry.SignalOutbound(sessionId, SignalResult.Input(
                                ToolResponse.Complete(assistantText)));
                        }
                        else
                        {
                            _logger.LogWarning(
                                "Agent for session {SessionId} ended its turn without calling any signaling tool.",
                                sessionId);
                            _signalRegistry.SignalOutbound(sessionId, SignalResult.Input(
                                ToolResponse.Error("Agent ended its turn without calling any signaling tool.")));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to post terminal signal for session {SessionId}", sessionId);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Converts an input signal from the agent into a ToolResponse.
    /// </summary>
    private static ToolResponse ConvertInputSignal(object? data)
    {
        if (data is ToolResponse toolResponse)
        {
            return toolResponse;
        }

        if (data is string inputQuestion)
        {
            return ToolResponse.InputRequested(inputQuestion);
        }

        // Handle structured input request with options
        if (data is Dictionary<string, object> dict)
        {
            if (dict.TryGetValue("question", out var questionObj) && questionObj is string question)
            {
                List<string>? options = null;
                if (dict.TryGetValue("options", out var optionsObj) && optionsObj is List<string> optionsList)
                {
                    options = optionsList;
                }
                return ToolResponse.InputRequested(question, options);
            }
        }

        return ToolResponse.InputRequested("User input required", null);
    }

    /// <summary>
    /// Validates reasoning effort configuration for a model and logs a warning if not supported.
    /// As per design doc: "The bridge checks IAgentProvider.SupportsReasoningEffort(model)
    /// and ignores the setting if unsupported (logged as warning)."
    /// </summary>
    private async Task ValidateReasoningEffortAsync(string model, string reasoningEffort, string toolName, CancellationToken ct)
    {
        // First check the synchronous method
        if (!_agentProvider.SupportsReasoningEffort(model))
        {
            _logger.LogWarning(
                "Model '{Model}' does not support reasoning effort configuration. " +
                "The reasoningEffort setting '{ReasoningEffort}' for tool '{ToolName}' will be ignored.",
                model, reasoningEffort, toolName);
            return;
        }

        // Additionally, query runtime capabilities for validation
        var capabilities = await _agentProvider.GetModelCapabilitiesAsync(model, ct);
        if (capabilities != null)
        {
            if (!capabilities.SupportsReasoningEffort)
            {
                _logger.LogWarning(
                    "Runtime capability check: Model '{Model}' does not support reasoning effort. " +
                    "The reasoningEffort setting '{ReasoningEffort}' for tool '{ToolName}' will be ignored.",
                    model, reasoningEffort, toolName);
            }
            else if (capabilities.SupportedReasoningLevels.Count > 0 &&
                     !capabilities.SupportedReasoningLevels.Contains(reasoningEffort, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Model '{Model}' does not support reasoning effort level '{ReasoningEffort}'. " +
                    "Supported levels: [{SupportedLevels}]. The setting for tool '{ToolName}' may be adjusted.",
                    model, reasoningEffort, string.Join(", ", capabilities.SupportedReasoningLevels), toolName);
            }
        }
    }
}
