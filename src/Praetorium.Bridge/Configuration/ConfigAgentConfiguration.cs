using System.Text.Json.Serialization;

namespace Praetorium.Bridge.Configuration;

/// <summary>
/// Configuration for the dashboard's interactive Config Agent — a per-user chat agent
/// that reads and modifies the bridge configuration through a staged change set.
/// </summary>
public class ConfigAgentConfiguration
{
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("reasoningEffort")]
    public string? ReasoningEffort { get; set; }

    [JsonPropertyName("promptFile")]
    public string? PromptFile { get; set; }
}
