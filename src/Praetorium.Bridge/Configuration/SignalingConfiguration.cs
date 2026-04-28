using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Praetorium.Bridge.Configuration;

/// <summary>
/// Configuration for signaling tools exposed to the agent.
/// </summary>
public class SignalingConfiguration
{
    /// <summary>
    /// The interval in seconds for sending keepalive signals.
    /// </summary>
    [JsonPropertyName("keepaliveIntervalSeconds")]
    public int KeepaliveIntervalSeconds { get; set; } = 15;

    /// <summary>
    /// The signaling tool entries available to the agent. Both default (respond, request_input,
    /// await_signal) and custom tools are represented as editable entries in this list.
    /// </summary>
    [JsonPropertyName("tools")]
    public List<SignalingToolEntry> Tools { get; set; } = new();
}

/// <summary>
/// Definition of a signaling tool exposed to the agent.
/// </summary>
public class SignalingToolEntry
{
    /// <summary>
    /// The name of the signaling tool. For defaults this is one of "respond", "request_input",
    /// or "await_signal"; for custom signals this is any valid tool name.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// A description of when the agent should invoke this signaling tool.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Parameters the agent passes when invoking this signaling tool.
    /// </summary>
    [JsonPropertyName("parameters")]
    public Dictionary<string, ParameterDefinition> Parameters { get; set; } = new();

    /// <summary>
    /// When true, the tool blocks the agent until a response signal is received.
    /// When false, the tool is fire-and-forget and returns immediately after dispatch.
    /// </summary>
    [JsonPropertyName("isBlocking")]
    public bool IsBlocking { get; set; }

    /// <summary>
    /// When true, the agent — once parked inside this blocking signaling tool — is
    /// considered open to accept a brand-new prompt from the external caller. The
    /// dispatcher then promotes a follow-up payload-bearing tool invocation from
    /// <c>Rejoin</c> to <c>Resume</c>, packing plain <c>Prompt</c>-kind parameters
    /// into the resume payload so the parked tool's inbound waiter is unblocked
    /// with the caller's input.
    /// <para>
    /// Tools that require a specific reply (e.g. <c>request_input</c>,
    /// <c>await_signal</c>) must leave this <c>false</c> so that an unrelated
    /// follow-up call is treated as a pure rejoin instead of being misinterpreted
    /// as the awaited reply.
    /// </para>
    /// <para>
    /// Only valid when <see cref="IsBlocking"/> is <c>true</c>; setting it on a
    /// non-blocking tool is a configuration error and is rejected at tool
    /// construction time.
    /// </para>
    /// </summary>
    [JsonPropertyName("acceptsNewPrompt")]
    public bool AcceptsNewPrompt { get; set; }

    /// <summary>
    /// Response format for blocking tools. Either "json" (structured payload matching
    /// <see cref="ResponseParameters"/>) or "markdown" (rendered via <see cref="ResponsePromptFile"/>).
    /// Null when the tool is non-blocking.
    /// </summary>
    [JsonPropertyName("responseFormat")]
    public string? ResponseFormat { get; set; }

    /// <summary>
    /// When <see cref="ResponseFormat"/> is "json", the schema of the fields the tool will
    /// return to the agent. Parameter names and descriptions are surfaced in the tool schema
    /// so the model knows what to expect back.
    /// </summary>
    [JsonPropertyName("responseParameters")]
    public Dictionary<string, ParameterDefinition>? ResponseParameters { get; set; }

    /// <summary>
    /// When <see cref="ResponseFormat"/> is "markdown", the prompt template file whose
    /// contents are returned to the agent as the formatted response.
    /// </summary>
    [JsonPropertyName("responsePromptFile")]
    public string? ResponsePromptFile { get; set; }

    /// <summary>
    /// Format of the payload sent from the agent *out* to the external caller when this
    /// signaling tool fires. "json" (default) delivers the agent's structured parameters;
    /// "markdown" renders <see cref="OutgoingPromptFile"/> with the agent's parameters as
    /// template variables.
    /// </summary>
    [JsonPropertyName("outgoingFormat")]
    public string? OutgoingFormat { get; set; }

    /// <summary>
    /// When <see cref="OutgoingFormat"/> is "markdown", the prompt template file whose
    /// contents are returned to the external caller as the outgoing payload.
    /// </summary>
    [JsonPropertyName("outgoingPromptFile")]
    public string? OutgoingPromptFile { get; set; }
}
