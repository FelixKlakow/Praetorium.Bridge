using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using Praetorium.Bridge.Configuration;
using Praetorium.Bridge.Mcp;
using Praetorium.Bridge.Prompts;
using Praetorium.Bridge.Tools;

namespace Praetorium.Bridge.Signaling;

/// <summary>
/// Factory for creating signaling tool definitions based on configuration.
/// </summary>
public static class SignalingToolFactory
{
    public const string RespondToolName = "respond";
    public const string RequestInputToolName = "request_input";
    public const string AwaitSignalToolName = "await_signal";

    public const string JsonResponseFormat = "json";
    public const string MarkdownResponseFormat = "markdown";

    private static readonly TimeSpan DefaultSignalWaitTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultKeepaliveInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Creates a list of signaling tool definitions from the configuration.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session these tools are for.</param>
    /// <param name="config">The signaling configuration.</param>
    /// <param name="signalRegistry">The signal registry the tools dispatch through.</param>
    /// <param name="promptDirectory">Directory used to resolve markdown prompt files.</param>
    public static List<SignalingToolDefinition> CreateSignalingTools(
        string sessionId,
        SignalingConfiguration config,
        ISignalRegistry signalRegistry,
        string promptDirectory)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        if (config == null)
            throw new ArgumentNullException(nameof(config));

        if (signalRegistry == null)
            throw new ArgumentNullException(nameof(signalRegistry));

        if (string.IsNullOrEmpty(promptDirectory))
            throw new ArgumentException("Prompt directory is required.", nameof(promptDirectory));

        var keepaliveInterval = config.KeepaliveIntervalSeconds > 0
            ? TimeSpan.FromSeconds(config.KeepaliveIntervalSeconds)
            : DefaultKeepaliveInterval;

        var tools = new List<SignalingToolDefinition>(config.Tools.Count);
        foreach (var entry in config.Tools)
        {
            tools.Add(CreateTool(sessionId, entry, signalRegistry, promptDirectory, keepaliveInterval));
        }

        return tools;
    }

    private static SignalingToolDefinition CreateTool(
        string sessionId,
        SignalingToolEntry entry,
        ISignalRegistry signalRegistry,
        string promptDirectory,
        TimeSpan keepaliveInterval)
    {
        return entry.Name switch
        {
            RespondToolName => CreateRespondTool(sessionId, entry, signalRegistry, promptDirectory),
            RequestInputToolName => CreateRequestInputTool(sessionId, entry, signalRegistry, promptDirectory, keepaliveInterval),
            AwaitSignalToolName => CreateAwaitSignalTool(sessionId, entry, signalRegistry, keepaliveInterval),
            _ => CreateCustomTool(sessionId, entry, signalRegistry, promptDirectory, keepaliveInterval),
        };
    }

    private static SignalingToolDefinition CreateRespondTool(
        string sessionId,
        SignalingToolEntry entry,
        ISignalRegistry signalRegistry,
        string promptDirectory)
    {
        var schema = ConvertParametersToSchema(entry.Parameters);

        Task<string> Handler(JsonElement parameters, IProgress<ProgressNotificationValue>? progress, CancellationToken ct)
        {
            _ = progress; // respond is non-blocking; no keepalive needed.

            var message = parameters.TryGetProperty("message", out var msgProp)
                ? msgProp.GetString()
                : null;

            if (string.IsNullOrEmpty(message))
                return Task.FromResult("Error: 'message' parameter is required and must be a non-empty string.");

            object? metadata = null;
            if (parameters.TryGetProperty("metadata", out var metaProp) && metaProp.ValueKind != JsonValueKind.Null)
            {
                metadata = JsonSerializer.Deserialize<object>(metaProp.GetRawText());
            }

            var outgoing = IsMarkdown(entry.OutgoingFormat)
                ? ToolResponse.Complete(RenderMarkdown(promptDirectory, entry.OutgoingPromptFile, parameters))
                : ToolResponse.Complete(message, metadata);

            signalRegistry.SignalOutbound(sessionId, SignalResult.Input(outgoing));
            return Task.FromResult($"Response sent: {message}");
        }

        return new SignalingToolDefinition(entry.Name, entry.Description, schema, Handler);
    }

    private static SignalingToolDefinition CreateRequestInputTool(
        string sessionId,
        SignalingToolEntry entry,
        ISignalRegistry signalRegistry,
        string promptDirectory,
        TimeSpan keepaliveInterval)
    {
        var schema = ConvertParametersToSchema(entry.Parameters);

        async Task<string> Handler(JsonElement parameters, IProgress<ProgressNotificationValue>? progress, CancellationToken ct)
        {
            var question = parameters.TryGetProperty("question", out var qProp)
                ? qProp.GetString()
                : null;

            if (string.IsNullOrEmpty(question))
                return "Error: 'question' parameter is required and must be a non-empty string.";

            List<string>? options = null;
            if (parameters.TryGetProperty("options", out var optProp) && optProp.ValueKind == JsonValueKind.Array)
            {
                options = new List<string>();
                foreach (var item in optProp.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                        options.Add(item.GetString() ?? string.Empty);
                }
            }

            // Outgoing payload: structured input_requested, or markdown-rendered question.
            var outgoing = IsMarkdown(entry.OutgoingFormat)
                ? ToolResponse.Complete(RenderMarkdown(promptDirectory, entry.OutgoingPromptFile, parameters))
                : ToolResponse.InputRequested(question, options);

            signalRegistry.SignalOutbound(sessionId, SignalResult.Input(outgoing));

            var inputSignal = await WaitInboundWithKeepaliveAsync(
                signalRegistry, sessionId, progress, keepaliveInterval, "waiting for caller input", ct)
                .ConfigureAwait(false);

            return FormatBlockingResponse(entry, inputSignal, promptDirectory);
        }

        return new SignalingToolDefinition(entry.Name, entry.Description, schema, Handler);
    }

    private static SignalingToolDefinition CreateAwaitSignalTool(
        string sessionId,
        SignalingToolEntry entry,
        ISignalRegistry signalRegistry,
        TimeSpan keepaliveInterval)
    {
        var schema = ConvertParametersToSchema(entry.Parameters);

        async Task<string> Handler(JsonElement parameters, IProgress<ProgressNotificationValue>? progress, CancellationToken ct)
        {
            var signal = await WaitInboundWithKeepaliveAsync(
                signalRegistry, sessionId, progress, keepaliveInterval, "waiting for caller signal", ct)
                .ConfigureAwait(false);

            return JsonSerializer.Serialize(new
            {
                type = signal.Type.ToString(),
                data = signal.Data,
            });
        }

        return new SignalingToolDefinition(entry.Name, entry.Description, schema, Handler);
    }

    private static SignalingToolDefinition CreateCustomTool(
        string sessionId,
        SignalingToolEntry entry,
        ISignalRegistry signalRegistry,
        string promptDirectory,
        TimeSpan keepaliveInterval)
    {
        var schema = ConvertParametersToSchema(entry.Parameters);

        async Task<string> Handler(JsonElement parameters, IProgress<ProgressNotificationValue>? progress, CancellationToken ct)
        {
            // Outgoing payload to the external caller.
            ToolResponse outgoing;
            if (IsMarkdown(entry.OutgoingFormat))
            {
                outgoing = ToolResponse.Complete(RenderMarkdown(promptDirectory, entry.OutgoingPromptFile, parameters));
            }
            else
            {
                // JSON: hand the agent's raw parameters to the caller as metadata.
                var packed = parameters.ValueKind == JsonValueKind.Undefined
                    ? null
                    : JsonSerializer.Deserialize<object>(parameters.GetRawText());
                outgoing = ToolResponse.Complete(message: $"Signal '{entry.Name}' dispatched.", metadata: packed);
            }

            signalRegistry.SignalOutbound(sessionId, SignalResult.Input(outgoing));

            if (!entry.IsBlocking)
                return $"Signal '{entry.Name}' dispatched.";

            var signal = await WaitInboundWithKeepaliveAsync(
                signalRegistry, sessionId, progress, keepaliveInterval, $"waiting for '{entry.Name}' response", ct)
                .ConfigureAwait(false);

            return FormatBlockingResponse(entry, signal, promptDirectory);
        }

        return new SignalingToolDefinition(entry.Name, entry.Description, schema, Handler);
    }

    /// <summary>
    /// Awaits the next inbound signal for a session while emitting periodic keepalive progress
    /// notifications so the agent-side caller knows the external side is still engaged.
    /// </summary>
    private static async Task<SignalResult> WaitInboundWithKeepaliveAsync(
        ISignalRegistry signalRegistry,
        string sessionId,
        IProgress<ProgressNotificationValue>? progress,
        TimeSpan keepaliveInterval,
        string keepaliveMessage,
        CancellationToken ct)
    {
        if (progress == null)
        {
            return await signalRegistry
                .WaitInboundAsync(sessionId, DefaultSignalWaitTimeout, ct)
                .ConfigureAwait(false);
        }

        using var reporter = new ProgressReporter(progress, keepaliveInterval, keepaliveMessage);
        reporter.Start();
        try
        {
            return await signalRegistry
                .WaitInboundAsync(sessionId, DefaultSignalWaitTimeout, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            reporter.Stop();
        }
    }

    /// <summary>
    /// Formats a signal received by a blocking signaling tool into the string handed back
    /// to the agent — governed by <see cref="SignalingToolEntry.ResponseFormat"/>.
    /// </summary>
    private static string FormatBlockingResponse(
        SignalingToolEntry entry,
        SignalResult signal,
        string promptDirectory)
    {
        if (signal.Type == SignalType.Timeout)
            return $"Error: timed out waiting for '{entry.Name}' response.";
        if (signal.Type == SignalType.Disconnect)
            return "Error: Caller disconnected.";
        if (signal.Type == SignalType.Reset)
            return "Error: Session reset.";

        return IsMarkdown(entry.ResponseFormat)
            ? LoadMarkdownResponse(promptDirectory, entry.ResponsePromptFile)
            : FormatSignalResult(signal);
    }

    private static string FormatSignalResult(SignalResult signal)
    {
        return signal.Type switch
        {
            SignalType.Input when signal.Data != null => JsonSerializer.Serialize(signal.Data),
            SignalType.Timeout => "Error: Request timeout - no input received.",
            SignalType.Disconnect => "Error: Caller disconnected.",
            SignalType.Reset => "Error: Session reset.",
            _ => "Error: Invalid signal received.",
        };
    }

    private static bool IsMarkdown(string? format) =>
        string.Equals(format, MarkdownResponseFormat, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Renders a markdown prompt file using the agent-supplied parameters as placeholder vars.
    /// Used for the outgoing (to external caller) payload.
    /// </summary>
    private static string RenderMarkdown(string promptDirectory, string? promptFile, JsonElement parameters)
    {
        if (string.IsNullOrWhiteSpace(promptFile))
            throw new InvalidOperationException(
                "Signaling tool is configured with markdown outgoing format but no outgoingPromptFile is set.");

        var fullPath = ResolvePromptPath(promptDirectory, promptFile);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Outgoing prompt file '{promptFile}' does not exist.", fullPath);

        var template = File.ReadAllText(fullPath);

        var vars = new Dictionary<string, JsonElement>();
        if (parameters.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in parameters.EnumerateObject())
                vars[prop.Name] = prop.Value.Clone();
        }

        return PlaceholderEngine.Render(template, vars);
    }

    /// <summary>
    /// Loads a markdown response file verbatim (no templating) and returns it to the agent
    /// as the blocking response payload.
    /// </summary>
    private static string LoadMarkdownResponse(string promptDirectory, string? promptFile)
    {
        if (string.IsNullOrWhiteSpace(promptFile))
            throw new InvalidOperationException(
                "Signaling tool is configured with markdown response format but no responsePromptFile is set.");

        var fullPath = ResolvePromptPath(promptDirectory, promptFile);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Response prompt file '{promptFile}' does not exist.", fullPath);

        return File.ReadAllText(fullPath);
    }

    private static string ResolvePromptPath(string promptDirectory, string promptFile)
    {
        var normalized = promptFile.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized.Substring(2);
        if (normalized.StartsWith("prompts/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring("prompts/".Length);

        var fullPath = Path.GetFullPath(Path.Combine(promptDirectory, normalized));
        var boundary = Path.GetFullPath(promptDirectory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Prompt path '{promptFile}' resolves outside the prompts directory.");

        return fullPath;
    }

    private static JsonElement ConvertParametersToSchema(Dictionary<string, ParameterDefinition> parameters)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var kvp in parameters)
        {
            var paramDef = kvp.Value;
            var prop = new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(paramDef.Type))
                prop["type"] = paramDef.Type;

            if (!string.IsNullOrEmpty(paramDef.Description))
                prop["description"] = paramDef.Description;

            if (paramDef.Items != null)
            {
                var itemsDict = new Dictionary<string, object>();
                if (!string.IsNullOrEmpty(paramDef.Items.Type))
                    itemsDict["type"] = paramDef.Items.Type;
                prop["items"] = itemsDict;
            }

            if (paramDef.Properties != null && paramDef.Properties.Count > 0)
                prop["properties"] = ConvertParameterProperties(paramDef.Properties);

            properties[kvp.Key] = prop;

            if (paramDef.Required)
                required.Add(kvp.Key);
        }

        var schema = new Dictionary<string, object>
        {
            { "type", "object" },
            { "properties", properties },
        };

        if (required.Count > 0)
            schema["required"] = required;

        var json = JsonSerializer.Serialize(schema);
        return JsonDocument.Parse(json).RootElement;
    }

    private static Dictionary<string, object> ConvertParameterProperties(
        Dictionary<string, ParameterDefinition> parameterDefinitions)
    {
        var result = new Dictionary<string, object>();
        foreach (var kvp in parameterDefinitions)
        {
            var paramDef = kvp.Value;
            var prop = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(paramDef.Type))
                prop["type"] = paramDef.Type;
            if (!string.IsNullOrEmpty(paramDef.Description))
                prop["description"] = paramDef.Description;
            result[kvp.Key] = prop;
        }
        return result;
    }
}
