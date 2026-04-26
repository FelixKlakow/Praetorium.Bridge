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

            // Step 2: Resolve configuration slices used throughout the dispatch.
            var agentConfig = toolDef.Agent ?? config.Defaults?.Agent ?? throw new InvalidOperationException("No agent configuration available");
            var sessionConfig = toolDef.Session ?? config.Defaults?.Session ?? new SessionConfiguration();
            var signalingConfig = toolDef.Signaling ?? config.Defaults?.Signaling ?? new SignalingConfiguration();
            var systemPromptConfig = toolDef.SystemPrompt ?? config.Defaults?.SystemPrompt
                ?? throw new InvalidOperationException("No system prompt configuration available");
            var sessionMode = sessionConfig.Mode;

            // Step 3: Pre-extract reserved parameters so we can locate the session BEFORE
            // enforcing turn-phase-sensitive required-parameter validation.
            ToolCallContext peekContext;
            try
            {
                peekContext = _binder.Bind(toolDef, arguments, TurnPhase.Rejoin);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Parameter extraction failed for tool '{ToolName}'", toolName);
                return ToolResponse.Error($"Parameter validation failed: {ex.Message}");
            }

            // Step 3a: Validate reasoning effort configuration
            var model = agentConfig.Model ?? "claude-sonnet-4.6";
            if (!string.IsNullOrEmpty(agentConfig.ReasoningEffort))
            {
                await ValidateReasoningEffortAsync(model, agentConfig.ReasoningEffort, toolName, ct);
            }

            var (sessionInfo, isNew) = await _sessionManager.GetOrCreateSessionAsync(
                toolName,
                peekContext.ReferenceId,
                connectionId,
                sessionMode,
                agentConfig,
                ct);

            _logger.LogInformation("Session {SessionId} obtained (new={IsNew}) for tool '{ToolName}'", sessionInfo.SessionId, isNew, toolName);

            if (connectionId != null)
            {
                _signalRegistry.RegisterConnectionBinding(sessionInfo.SessionId, connectionId);
            }

            // Step 4: Determine the turn phase from session state and caller payload.
            //   NewTurn — no running turn (or previous one ended).
            //   Resume  — a running turn AND the caller supplied a payload (resume params
            //             or _input). Posting on a not-yet-registered inbound waiter is
            //             safe: SignalRegistry buffers FIFO until the agent reads it.
            //   Rejoin  — a running turn AND the caller supplied no payload (pure poll).
            //
            // NOTE: The phase MUST NOT depend on whether the agent has already registered
            // an inbound waiter. Between the agent emitting an outbound signal and the
            // agent's blocking handler reaching its WaitInboundAsync call, there is a
            // race window during which the external caller can arrive — gating Resume
            // on waiter presence would silently drop the caller's input in that window.
            var existingTurn = isNew ? null : _sessionManager.GetRunningTurn(sessionInfo.SessionId);
            var hasRunningTurn = existingTurn != null && !existingTurn.IsCompleted;
            TurnPhase phase;
            if (!hasRunningTurn)
            {
                phase = TurnPhase.NewTurn;
            }
            else if (CallerProvidedResumePayload(toolDef, peekContext))
            {
                phase = TurnPhase.Resume;
            }
            else
            {
                phase = TurnPhase.Rejoin;
            }

            // Step 5: Re-bind with the phase so required-parameter enforcement
            // matches the actual dispatch semantics.
            ToolCallContext toolContext;
            try
            {
                toolContext = _binder.Bind(toolDef, arguments, phase);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Parameter binding failed for tool '{ToolName}' in phase {Phase}", toolName, phase);
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

            // Step 6: Dispatch based on phase.
            switch (phase)
            {
                case TurnPhase.NewTurn:
                {
                    var initErr = await StartNewTurnAsync(
                        sessionInfo, isNew, toolName, toolDef, toolContext,
                        agentConfig, signalingConfig, systemPromptConfig, config, ct);
                    if (initErr != null) return initErr;
                    break;
                }

                case TurnPhase.Resume:
                {
                    _logger.LogInformation("Resuming blocked turn for session {SessionId}", sessionInfo.SessionId);
                    var resumePayload = BuildResumePayload(toolDef, toolContext);
                    _signalRegistry.SignalInbound(sessionInfo.SessionId, SignalResult.Input(resumePayload));
                    break;
                }

                case TurnPhase.Rejoin:
                {
                    _logger.LogInformation("Rejoining running turn for session {SessionId}", sessionInfo.SessionId);
                    // Pure poll — nothing to post. Caller subscribes for the next outbound signal.
                    break;
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

            // Step 9a: If the turn is still running OR more outbound signals are
            // already queued behind this one, demote a terminal 'complete' to
            // 'partial' so the caller knows to invoke the tool again to keep
            // draining (non-blocking streaming case, or agent contract violation
            // where 'complete' was emitted before the final signal).
            var turnAfterSignal = _sessionManager.GetRunningTurn(sessionInfo.SessionId);
            var turnStillAlive = turnAfterSignal != null && !turnAfterSignal.IsCompleted;
            var moreOutboundQueued = _signalRegistry.HasPendingOutbound(sessionInfo.SessionId);
            if ((turnStillAlive || moreOutboundQueued) && response.Status == "complete")
            {
                response = ToolResponse.Partial(response.Message, response.Metadata);
            }

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

    /// <summary>
    /// Starts a fresh agent turn: resolves the tool prompt, optionally creates the
    /// agent session (on first dispatch), wires the outbound-signal tracker, and kicks
    /// off <see cref="IAgentSession.SendAsync"/> as a background turn task. The rendered
    /// tool prompt is sent as the user message; the static system prompt (installed on
    /// agent creation) governs tool-calling conventions only.
    /// </summary>
    /// <returns>
    /// <c>null</c> on success, or an error <see cref="ToolResponse"/> that the caller
    /// should return directly.
    /// </returns>
    private async Task<ToolResponse?> StartNewTurnAsync(
        SessionInfo sessionInfo,
        bool isNew,
        string toolName,
        ToolDefinition toolDef,
        ToolCallContext toolContext,
        AgentConfiguration agentConfig,
        SignalingConfiguration signalingConfig,
        SystemPromptConfiguration systemPromptConfig,
        BridgeConfiguration config,
        CancellationToken ct)
    {
        // Render the per-turn tool prompt (this is the user message).
        string toolPrompt;
        try
        {
            toolPrompt = await _promptResolver.ResolveAsync(toolName, toolDef, toolContext.BoundParameters, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Prompt resolution failed for tool '{ToolName}'", toolName);
            await _sessionManager.MarkCrashedAsync(sessionInfo.SessionId, ex, ct);
            return ToolResponse.Error($"Prompt resolution failed: {ex.Message}");
        }

        IAgentSession agentSession;
        try
        {
            if (isNew)
            {
                // Resolve the static system prompt: inline Content XOR PromptFile.
                string systemPromptText;
                try
                {
                    systemPromptText = ResolveSystemPrompt(systemPromptConfig);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "System prompt resolution failed for tool '{ToolName}'", toolName);
                    await _sessionManager.MarkCrashedAsync(sessionInfo.SessionId, ex, ct);
                    return ToolResponse.Error($"System prompt resolution failed: {ex.Message}");
                }

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
                    systemPromptText,
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
            // any signal the agent emits synchronously — including before the first
            // await yields — is counted.
            var tracker = BeginTrackTurn(sessionInfo.SessionId);

            Task<string> turnTask;
            try
            {
                turnTask = agentSession.SendAsync(toolPrompt, ct);
            }
            catch
            {
                tracker.Detach(_signalRegistry);
                throw;
            }
            CompleteTrackTurn(sessionInfo.SessionId, turnTask, tracker);

            _logger.LogInformation("Tool prompt sent to session {SessionId}", sessionInfo.SessionId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start agent turn for session {SessionId}", sessionInfo.SessionId);
            await _sessionManager.MarkCrashedAsync(sessionInfo.SessionId, ex, ct);
            return ToolResponse.Error($"Failed to start agent turn: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves a <see cref="SystemPromptConfiguration"/> to its final text. Content and
    /// PromptFile are mutually exclusive; exactly one must be set. PromptFile is resolved
    /// relative to <see cref="IConfigurationProvider.ConfigDirectory"/> under
    /// <c>prompts/</c>, with the same path-containment check as signaling tool prompts.
    /// </summary>
    private string ResolveSystemPrompt(SystemPromptConfiguration systemPromptConfig)
    {
        if (systemPromptConfig == null)
            throw new ArgumentNullException(nameof(systemPromptConfig));

        var hasContent = !string.IsNullOrEmpty(systemPromptConfig.Content);
        var hasFile = !string.IsNullOrWhiteSpace(systemPromptConfig.PromptFile);

        if (hasContent && hasFile)
        {
            throw new InvalidOperationException(
                "SystemPromptConfiguration.Content and SystemPromptConfiguration.PromptFile are mutually exclusive; only one may be set.");
        }
        if (!hasContent && !hasFile)
        {
            throw new InvalidOperationException(
                "SystemPromptConfiguration requires either Content or PromptFile to be set.");
        }

        if (hasContent)
        {
            return systemPromptConfig.Content!;
        }

        var promptDirectory = Path.Combine(_configurationProvider.ConfigDirectory, "prompts");
        var normalized = systemPromptConfig.PromptFile!.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized.Substring(2);
        if (normalized.StartsWith("prompts/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring("prompts/".Length);

        var fullPath = Path.GetFullPath(Path.Combine(promptDirectory, normalized));
        var boundary = Path.GetFullPath(promptDirectory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"System prompt path '{systemPromptConfig.PromptFile}' resolves outside the prompts directory.");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"System prompt file '{systemPromptConfig.PromptFile}' does not exist.", fullPath);
        }

        return File.ReadAllText(fullPath);
    }

    /// <summary>
    /// Returns <c>true</c> when the caller's arguments carry a payload meant to
    /// unblock a parked agent — either a value for any <see cref="ParameterKind.Resume"/>
    /// parameter declared by the tool, or a non-empty reserved <c>_input</c>. Used
    /// by phase detection so a Resume vs. Rejoin classification depends on the
    /// caller's intent rather than on whether the agent has already registered an
    /// inbound waiter (which is racy).
    /// </summary>
    private static bool CallerProvidedResumePayload(ToolDefinition toolDef, ToolCallContext peekContext)
    {
        if (toolDef.Parameters != null)
        {
            foreach (var kvp in toolDef.Parameters)
            {
                if (kvp.Value.Kind != ParameterKind.Resume)
                    continue;
                if (peekContext.BoundParameters.ContainsKey(kvp.Key))
                    return true;
            }
        }

        return !string.IsNullOrEmpty(peekContext.Input);
    }

    /// <summary>
    /// Packs a payload for <see cref="TurnPhase.Resume"/> dispatches. When the tool
    /// declares <see cref="ParameterKind.Resume"/> parameters, they are packed as a
    /// JSON object so the parked signaling tool can unpack structured data. Otherwise
    /// the caller's <c>_input</c> string (if any) is forwarded verbatim.
    /// </summary>
    private static object BuildResumePayload(ToolDefinition toolDef, ToolCallContext toolContext)
    {
        if (toolDef.Parameters != null)
        {
            var resumeParams = new Dictionary<string, JsonElement>();
            foreach (var kvp in toolDef.Parameters)
            {
                if (kvp.Value.Kind != ParameterKind.Resume)
                    continue;
                if (toolContext.BoundParameters.TryGetValue(kvp.Key, out var value))
                {
                    resumeParams[kvp.Key] = value;
                }
            }

            if (resumeParams.Count > 0)
            {
                return resumeParams;
            }
        }

        return toolContext.Input ?? string.Empty;
    }
}
