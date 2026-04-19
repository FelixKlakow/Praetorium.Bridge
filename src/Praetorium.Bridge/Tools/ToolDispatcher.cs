using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Praetorium.Bridge.Agents;
using Praetorium.Bridge.Configuration;
using Praetorium.Bridge.Hooks;
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

    /// <summary>
    /// Dispatches a tool call to the appropriate agent session.
    /// </summary>
    public async Task<ToolResponse> DispatchAsync(string toolName, JsonElement arguments, string? connectionId, CancellationToken ct)
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

            // Step 3: If input is provided, signal existing session with input
            if (toolContext.Input != null)
            {
                // This is a continuation; find the session that's waiting for input
                _logger.LogInformation("Input provided, signaling waiting session");
                // In a real scenario, we'd need a way to find the session waiting for this input
                // For now, we'll proceed with normal dispatch
            }

            // Step 4: Reset session if requested
            if (toolContext.ResetSession)
            {
                _logger.LogInformation("Reset requested for tool '{ToolName}'", toolName);
                // Session will be reset during GetOrCreateSession
            }

            // Step 5: Determine session mode and get or create session
            var agentConfig = toolDef.Agent ?? config.Defaults?.Agent ?? throw new InvalidOperationException("No agent configuration available");
            var sessionConfig = toolDef.Session ?? config.Defaults?.Session ?? new SessionConfiguration();
            var sessionMode = sessionConfig.Mode;

            // Step 5a: Validate reasoning effort configuration
            var model = agentConfig.Model ?? "claude-sonnet-4-6";
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

            // Step 6: Resolve prompt
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

            // Step 7: Create AI agent if new session, get tool sources, and send prompt
            IAgentSession agentSession;
            try
            {
                if (isNew)
                {
                    // Create a new AI agent for this session
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

                    // Create signaling tools from configuration
                    var signalingConfig = toolDef.Signaling ?? config.Defaults?.Signaling ?? new SignalingConfiguration();
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
                    // Reactivate pooled session - get existing agent session
                    agentSession = _sessionManager.GetActiveAgent(sessionInfo.SessionId)
                        ?? throw new InvalidOperationException($"Session {sessionInfo.SessionId} has no active agent session");

                    // Note: OnSessionWokenAsync is already called in SessionManager.GetOrCreateSessionAsync
                }

                // Send the message via the agent session
                await agentSession.SendAsync(prompt, ct).ConfigureAwait(false);
                _logger.LogInformation("Prompt sent to session {SessionId}", sessionInfo.SessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send prompt to session {SessionId}", sessionInfo.SessionId);
                await _sessionManager.MarkCrashedAsync(sessionInfo.SessionId, ex, ct);
                return ToolResponse.Error($"Failed to initialize agent session: {ex.Message}");
            }

            // Step 8: Hook for tool invoked
            var hookParams = toolContext.BoundParameters
                .ToDictionary(kv => kv.Key, kv => (object?)kv.Value.ToString());
            await _hooks.OnToolInvokedAsync(
                new ToolInvocationContext(
                    Guid.NewGuid().ToString(),
                    toolName,
                    hookParams,
                    connectionId ?? "unknown"),
                ct);

            // Step 9: Wait for signal with timeout
            var responseTimeoutSeconds = sessionConfig.ResponseTimeoutSeconds > 0
                ? sessionConfig.ResponseTimeoutSeconds
                : 600;
            var timeout = TimeSpan.FromSeconds(responseTimeoutSeconds);

            SignalResult signal;
            try
            {
                signal = await _signalRegistry.WaitForSignalAsync(sessionInfo.SessionId, timeout, ct);
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

            // Step 10: Convert signal to response
            ToolResponse response = signal.Type switch
            {
                SignalType.Timeout => ToolResponse.Error($"Agent response timeout (>{timeout.TotalSeconds} seconds)"),
                SignalType.Disconnect => ToolResponse.Error("Connection was disconnected"),
                SignalType.Reset => ToolResponse.Error("Session was reset"),
                SignalType.Input => ConvertInputSignal(signal.Data),
                _ => ToolResponse.Error($"Unknown signal type: {signal.Type}")
            };

            // Step 11: Fire hooks
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
