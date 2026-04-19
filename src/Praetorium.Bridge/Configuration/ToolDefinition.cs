using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Praetorium.Bridge.Configuration;

/// <summary>
/// Configuration for an external tool exposed through the bridge.
/// </summary>
public class ToolDefinition
{
    /// <summary>
    /// A description of the tool's purpose and functionality.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Parameter definitions for this tool, keyed by parameter name.
    /// </summary>
    [JsonPropertyName("parameters")]
    public Dictionary<string, ParameterDefinition> Parameters { get; set; } = new();

    /// <summary>
    /// Fixed parameter values that are always passed to this tool.
    /// </summary>
    [JsonPropertyName("fixedParameters")]
    public Dictionary<string, JsonElement> FixedParameters { get; set; } = new();

    /// <summary>
    /// Agent configuration override for this specific tool.
    /// </summary>
    [JsonPropertyName("agent")]
    public AgentConfiguration? Agent { get; set; }

    /// <summary>
    /// Session configuration for this tool.
    /// </summary>
    [JsonPropertyName("session")]
    public SessionConfiguration? Session { get; set; }

    /// <summary>
    /// Signaling configuration for this tool.
    /// </summary>
    [JsonPropertyName("signaling")]
    public SignalingConfiguration? Signaling { get; set; }
}
