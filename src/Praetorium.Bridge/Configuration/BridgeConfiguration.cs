using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Praetorium.Bridge.Configuration;

/// <summary>
/// Root configuration model for the Praetorium Bridge.
/// </summary>
public class BridgeConfiguration
{
    /// <summary>
    /// Server configuration.
    /// </summary>
    [JsonPropertyName("server")]
    public ServerConfiguration Server { get; set; } = new();

    /// <summary>
    /// Default configuration values for agents, sessions, and signaling.
    /// </summary>
    [JsonPropertyName("defaults")]
    public DefaultsConfiguration Defaults { get; set; } = new();

    /// <summary>
    /// Tool definitions, keyed by tool name.
    /// </summary>
    [JsonPropertyName("tools")]
    public Dictionary<string, ToolDefinition> Tools { get; set; } = new();

    /// <summary>
    /// Agent tool sources, keyed by source name.
    /// </summary>
    [JsonPropertyName("agentToolSources")]
    public Dictionary<string, AgentToolSource> AgentToolSources { get; set; } = new();

    /// <summary>
    /// Configuration for the dashboard Config Agent (per-user, staged-changes chat).
    /// Optional: when absent the defaults.agent values are used.
    /// </summary>
    [JsonPropertyName("configAgent")]
    public ConfigAgentConfiguration? ConfigAgent { get; set; }
}
