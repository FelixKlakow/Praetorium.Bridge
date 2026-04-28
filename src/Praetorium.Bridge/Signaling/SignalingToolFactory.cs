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

    // Signaling tools wait indefinitely — they are released by the inbound signal,
    // by an explicit Reset (dashboard cancel), by Disconnect, or by the session's
    // cancellation token when the session is torn down. The IProgress<> keepalive
    // pings the caller so it knows the bridge is still alive during the wait.
    private static readonly TimeSpan DefaultSignalWaitTimeout = Timeout.InfiniteTimeSpan;
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

    /// <summary>
    /// Builds a signaling tool from a config entry. Behavior is fully driven by the entry —
    /// no name-based special cases. The well-known names (<see cref="RespondToolName"/>,
    /// <see cref="RequestInputToolName"/>, <see cref="AwaitSignalToolName"/>) exist only as
    /// default config templates the editor offers; the runtime treats every entry uniformly.
    /// </summary>
    private static SignalingToolDefinition CreateTool(
        string sessionId,
        SignalingToolEntry entry,
        ISignalRegistry signalRegistry,
        string promptDirectory,
        TimeSpan keepaliveInterval)
    {
        if (entry.AcceptsNewPrompt && !entry.IsBlocking)
        {
            throw new ArgumentException(
                $"Signaling tool '{entry.Name}' has acceptsNewPrompt=true but isBlocking=false. " +
                "acceptsNewPrompt is only meaningful for blocking signaling tools.",
                nameof(entry));
        }

        var schema = ConvertParametersToSchema(entry.Parameters);

        async Task<string> Handler(JsonElement parameters, IProgress<ProgressNotificationValue>? progress, CancellationToken ct)
        {
            // Outgoing payload to the external caller. Non-blocking signaling tools emit
            // 'partial' so the caller knows to re-invoke the tool to keep draining the
            // agent's output; blocking tools emit 'complete' because the tool is about
            // to park on the inbound waiter and the caller's reply is mandatory.
            bool isBlocking = entry.IsBlocking;
            ToolResponse outgoing;
            if (IsMarkdown(entry.OutgoingFormat))
            {
                var text = RenderMarkdown(promptDirectory, entry.OutgoingPromptFile, parameters);
                outgoing = isBlocking ? ToolResponse.Complete(text) : ToolResponse.Partial(text);
            }
            else
            {
                // JSON: hand the agent's raw parameters to the caller as metadata.
                var packed = parameters.ValueKind == JsonValueKind.Undefined
                    ? null
                    : JsonSerializer.Deserialize<object>(parameters.GetRawText());
                var message = $"Signal '{entry.Name}' dispatched.";
                outgoing = isBlocking
                    ? ToolResponse.Complete(message, packed)
                    : ToolResponse.Partial(message, packed);
            }

            signalRegistry.SignalOutbound(sessionId, SignalResult.Input(outgoing));

            if (!isBlocking)
                return $"Signal '{entry.Name}' dispatched.";

            // Record the parked tool's "open to new prompts" disposition so the
            // dispatcher's parked-agent fallback can decide whether a follow-up
            // payload-bearing call should unblock this waiter or stay a Rejoin.
            signalRegistry.BeginParkedSignalingTool(sessionId, entry.AcceptsNewPrompt);
            try
            {
                var signal = await WaitInboundWithKeepaliveAsync(
                    signalRegistry, sessionId, progress, keepaliveInterval, $"waiting for '{entry.Name}' response", ct)
                    .ConfigureAwait(false);

                return FormatBlockingResponse(entry, signal, promptDirectory);
            }
            finally
            {
                signalRegistry.EndParkedSignalingTool(sessionId);
            }
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
