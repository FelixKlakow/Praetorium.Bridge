using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Praetorium.Bridge.Configuration;

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

    /// <summary>
    /// Creates a list of signaling tool definitions from the configuration.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session these tools are for.</param>
    /// <param name="config">The signaling configuration.</param>
    /// <param name="signalRegistry">The signal registry the tools dispatch through.</param>
    /// <param name="promptDirectory">Directory used to resolve markdown response prompt files.</param>
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

        var tools = new List<SignalingToolDefinition>(config.Tools.Count);
        foreach (var entry in config.Tools)
        {
            tools.Add(CreateTool(sessionId, entry, signalRegistry, promptDirectory));
        }

        return tools;
    }

    private static SignalingToolDefinition CreateTool(
        string sessionId,
        SignalingToolEntry entry,
        ISignalRegistry signalRegistry,
        string promptDirectory)
    {
        return entry.Name switch
        {
            RespondToolName => CreateRespondTool(sessionId, entry, signalRegistry),
            RequestInputToolName => CreateRequestInputTool(sessionId, entry, signalRegistry),
            AwaitSignalToolName => CreateAwaitSignalTool(sessionId, entry, signalRegistry),
            _ => CreateCustomTool(sessionId, entry, signalRegistry, promptDirectory),
        };
    }

    private static SignalingToolDefinition CreateRespondTool(
        string sessionId,
        SignalingToolEntry entry,
        ISignalRegistry signalRegistry)
    {
        var schema = ConvertParametersToSchema(entry.Parameters);

        async Task<string> Handler(JsonElement parameters, CancellationToken ct)
        {
            var message = parameters.TryGetProperty("message", out var msgProp)
                ? msgProp.GetString()
                : null;

            if (string.IsNullOrEmpty(message))
                return "Error: 'message' parameter is required and must be a non-empty string.";

            object? metadata = null;
            if (parameters.TryGetProperty("metadata", out var metaProp) && metaProp.ValueKind != JsonValueKind.Null)
            {
                metadata = JsonSerializer.Deserialize<object>(metaProp.GetRawText());
            }

            signalRegistry.Signal(sessionId, SignalResult.Input(new { message, metadata }));
            return $"Response sent: {message}";
        }

        return new SignalingToolDefinition(entry.Name, entry.Description, schema, Handler);
    }

    private static SignalingToolDefinition CreateRequestInputTool(
        string sessionId,
        SignalingToolEntry entry,
        ISignalRegistry signalRegistry)
    {
        var schema = ConvertParametersToSchema(entry.Parameters);

        async Task<string> Handler(JsonElement parameters, CancellationToken ct)
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

            signalRegistry.Signal(sessionId, SignalResult.Input(new { question, options }));

            var inputSignal = await signalRegistry
                .WaitForSignalAsync(sessionId, DefaultSignalWaitTimeout, ct)
                .ConfigureAwait(false);

            return FormatSignalResult(inputSignal);
        }

        return new SignalingToolDefinition(entry.Name, entry.Description, schema, Handler);
    }

    private static SignalingToolDefinition CreateAwaitSignalTool(
        string sessionId,
        SignalingToolEntry entry,
        ISignalRegistry signalRegistry)
    {
        var schema = ConvertParametersToSchema(entry.Parameters);

        async Task<string> Handler(JsonElement parameters, CancellationToken ct)
        {
            var signal = await signalRegistry
                .WaitForSignalAsync(sessionId, DefaultSignalWaitTimeout, ct)
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
        string promptDirectory)
    {
        var schema = ConvertParametersToSchema(entry.Parameters);

        async Task<string> Handler(JsonElement parameters, CancellationToken ct)
        {
            signalRegistry.Signal(
                sessionId,
                SignalResult.Input(new { tool = entry.Name, parameters }));

            if (!entry.IsBlocking)
                return $"Signal '{entry.Name}' dispatched.";

            var signal = await signalRegistry
                .WaitForSignalAsync(sessionId, DefaultSignalWaitTimeout, ct)
                .ConfigureAwait(false);

            if (signal.Type == SignalType.Timeout)
                return $"Error: timed out waiting for '{entry.Name}' response.";
            if (signal.Type == SignalType.Disconnect)
                return "Error: Caller disconnected.";

            return entry.ResponseFormat switch
            {
                MarkdownResponseFormat => LoadMarkdownResponse(promptDirectory, entry.ResponsePromptFile),
                _ => FormatSignalResult(signal),
            };
        }

        return new SignalingToolDefinition(entry.Name, entry.Description, schema, Handler);
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

    private static string LoadMarkdownResponse(string promptDirectory, string? promptFile)
    {
        if (string.IsNullOrWhiteSpace(promptFile))
            throw new InvalidOperationException(
                "Signaling tool is configured with markdown response format but no responsePromptFile is set.");

        var normalized = promptFile.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized.Substring(2);
        if (normalized.StartsWith("prompts/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring("prompts/".Length);

        var fullPath = Path.GetFullPath(Path.Combine(promptDirectory, normalized));
        var boundary = Path.GetFullPath(promptDirectory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Response prompt path '{promptFile}' resolves outside the prompts directory.");

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Response prompt file '{promptFile}' does not exist.", fullPath);

        return File.ReadAllText(fullPath);
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
