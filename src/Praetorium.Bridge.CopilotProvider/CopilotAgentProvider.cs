using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;
using Praetorium.Bridge.Agents;
using Praetorium.Bridge.CopilotProvider.InternalMcp;

namespace Praetorium.Bridge.CopilotProvider;

/// <summary>
/// Configuration options for the Copilot agent provider.
/// </summary>
public class CopilotProviderOptions
{
    /// <summary>
    /// Gets or sets an explicit path to the Copilot CLI executable.
    /// When null, the SDK auto-detects from PATH and well-known installation locations.
    /// </summary>
    public string? CliPath { get; set; }

    /// <summary>
    /// Gets or sets the default model to use.
    /// </summary>
    public string DefaultModel { get; set; } = "claude-sonnet-4.6";

    /// <summary>
    /// Gets or sets the default instructions for agents.
    /// </summary>
    public string DefaultInstructions { get; set; } = "You are a helpful assistant.";
}

/// <summary>
/// Agent provider implementation using the GitHub Copilot SDK directly.
/// Uses local GitHub authentication (gh CLI, VS, or VS Code).
/// </summary>
public class CopilotAgentProvider : IAgentProvider
{
    private readonly CopilotProviderOptions _options;
    private readonly ILogger<CopilotAgentProvider> _logger;
    private readonly ConcurrentDictionary<string, AgentCapabilities> _capabilitiesCache = new();
    private readonly ConcurrentDictionary<string, ModelInfo> _sdkModelCache = new();
    private readonly CopilotClient _copilotClient;
    private readonly IInternalMcpRegistry _internalMcpRegistry;
    private readonly InternalMcpEndpoint _internalMcpEndpoint;

    /// <summary>
    /// Initializes a new instance of the CopilotAgentProvider class.
    /// </summary>
    /// <param name="options">Configuration options for the provider.</param>
    /// <param name="copilotClient">The shared CopilotClient instance (managed by DI).</param>
    /// <param name="internalMcpRegistry">Registry binding loopback session keys to signaling tools.</param>
    /// <param name="internalMcpEndpoint">Loopback-only MCP endpoint description.</param>
    /// <param name="logger">The logger for diagnostic output.</param>
    public CopilotAgentProvider(
        CopilotProviderOptions options,
        CopilotClient copilotClient,
        IInternalMcpRegistry internalMcpRegistry,
        InternalMcpEndpoint internalMcpEndpoint,
        ILogger<CopilotAgentProvider> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _copilotClient = copilotClient ?? throw new ArgumentNullException(nameof(copilotClient));
        _internalMcpRegistry = internalMcpRegistry ?? throw new ArgumentNullException(nameof(internalMcpRegistry));
        _internalMcpEndpoint = internalMcpEndpoint ?? throw new ArgumentNullException(nameof(internalMcpEndpoint));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The name of this agent provider.
    /// </summary>
    public string Name => "github-copilot";

    /// <summary>
    /// Creates a new agent session using the GitHub Copilot SDK.
    /// Authentication is handled automatically via local GitHub credentials.
    /// </summary>
    public async Task<IAgentSession> CreateAgentAsync(AgentContext context, CancellationToken ct)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        var model = context.AgentConfiguration.Model ?? _options.DefaultModel;

        // Validate reasoning effort support before creating session
        if (!string.IsNullOrEmpty(context.AgentConfiguration.ReasoningEffort))
        {
            var capabilities = await GetModelCapabilitiesAsync(model, ct).ConfigureAwait(false);
            if (capabilities != null && !capabilities.SupportsReasoningEffort)
            {
                _logger.LogWarning(
                    "Model '{Model}' does not support reasoning effort configuration. " +
                    "The reasoningEffort setting '{ReasoningEffort}' will be ignored.",
                    model,
                    context.AgentConfiguration.ReasoningEffort);
            }
        }

        var signalingTools = context.SignalingTools;

        // Route agent-side tool invocations through a loopback MCP server so the
        // Copilot SDK's built-in MCP client handles ProgressToken keepalives for
        // long-running signaling tools. The session key is random opaque state
        // so even another process on the loopback interface cannot guess it, and
        // a second random bearer token gates the request regardless.
        var sessionKey = RandomNumberGenerator.GetHexString(32, lowercase: true);
        var bearerToken = RandomNumberGenerator.GetHexString(64, lowercase: true);
        _internalMcpRegistry.Register(sessionKey, new InternalMcpRegistryEntry(signalingTools, bearerToken));

        var sessionConfig = new SessionConfig
        {
            Model = model,
            SystemMessage = new SystemMessageConfig
            {
                Mode = SystemMessageMode.Append,

                Content = context.SystemPrompt
            },
            OnPermissionRequest = PermissionHandler.ApproveAll,
            // Dictionary<string, object> is required because the Copilot SDK removed McpHttpServerConfig
            // as a typed base class in 0.2.2. McpRemoteServerConfig no longer shares a common base with
            // other server config types, so object is the only common type available without SDK changes.
            McpServers = new Dictionary<string, object>
            {
                ["praetorium-internal"] = new McpRemoteServerConfig
                {
                    Url = _internalMcpEndpoint.Url,
                    Type = "http",
                    Tools = ["*"],
                    Headers = new Dictionary<string, string>
                    {
                        [InternalMcpEndpoint.BearerTokenHeaderName] = bearerToken,
                        [InternalMcpEndpoint.SessionHeaderName] = sessionKey
                    }
                }
            }
        };

        if (!string.IsNullOrEmpty(context.AgentConfiguration.ReasoningEffort))
        {
            sessionConfig.ReasoningEffort = context.AgentConfiguration.ReasoningEffort;
        }

        CopilotSession session;
        try
        {
            session = await _copilotClient.CreateSessionAsync(sessionConfig).ConfigureAwait(false);
        }
        catch
        {
            _internalMcpRegistry.Unregister(sessionKey);
            throw;
        }

        _logger.LogInformation(
            "[CopilotAgentProvider] Created session for tool '{ToolName}' with model '{Model}'",
            context.ToolName, model);

        return new CopilotAgentSession(session, _internalMcpRegistry, sessionKey);
    }

    /// <summary>
    /// Determines whether this provider supports a specific model.
    /// </summary>
    public bool SupportsModel(string model)
    {
        if (string.IsNullOrEmpty(model))
            return false;

        return _sdkModelCache.ContainsKey(model);
    }

    /// <summary>
    /// Determines whether this provider supports reasoning effort configuration for a specific model.
    /// </summary>
    public bool SupportsReasoningEffort(string model)
    {
        if (string.IsNullOrEmpty(model))
            return false;

        if (_capabilitiesCache.TryGetValue(model, out var cached))
            return cached.SupportsReasoningEffort;

        if (_sdkModelCache.TryGetValue(model, out var sdkModel))
            return sdkModel.Capabilities?.Supports?.ReasoningEffort ?? false;

        return false;
    }

    /// <summary>
    /// Gets the capabilities of a specific model at runtime.
    /// </summary>
    public async Task<AgentCapabilities?> GetModelCapabilitiesAsync(string model, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(model))
            return null;

        if (_capabilitiesCache.TryGetValue(model, out var cached))
            return cached;

        await FetchAndCacheAllModelsAsync(ct).ConfigureAwait(false);

        if (!_sdkModelCache.TryGetValue(model, out var sdkModel))
            return null;

        return BuildCapabilities(sdkModel);
    }

    /// <summary>
    /// Gets all available model names.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct)
    {
        await FetchAndCacheAllModelsAsync(ct).ConfigureAwait(false);

        var names = new List<string>(_sdkModelCache.Keys);
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private async Task FetchAndCacheAllModelsAsync(CancellationToken ct)
    {
        var models = await _copilotClient.ListModelsAsync().ConfigureAwait(false);
        foreach (var model in models)
        {
            if (string.IsNullOrEmpty(model.Id))
                continue;

            if (_sdkModelCache.TryAdd(model.Id, model))
            {
                var capabilities = BuildCapabilities(model);
                _capabilitiesCache.TryAdd(model.Id, capabilities);
                _logger.LogDebug(
                    "Cached capabilities for model '{Model}': SupportsReasoningEffort={SupportsReasoning}, MaxContextWindowTokens={MaxTokens}",
                    model.Id,
                    capabilities.SupportsReasoningEffort,
                    capabilities.MaxTokens);
            }
        }
    }

    private static AgentCapabilities BuildCapabilities(ModelInfo model)
    {
        var supportsReasoning = model.Capabilities?.Supports?.ReasoningEffort ?? false;
        var maxTokens = model.Capabilities?.Limits?.MaxContextWindowTokens;

        return new AgentCapabilities
        {
            ModelName = model.Id,
            SupportsReasoningEffort = supportsReasoning,
            SupportedReasoningLevels = (IReadOnlyList<string>?)model.SupportedReasoningEfforts ?? [],
            MaxTokens = maxTokens > 0 ? maxTokens : null,
            SupportsToolCalling = true,
            SupportsStreaming = true,
            ProviderMetadata = new Dictionary<string, object>
            {
                ["providerName"] = "github-copilot",
                ["framework"] = "GitHub.Copilot.SDK"
            }
        };
    }
}
