using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Praetorium.Bridge.Configuration;

/// <summary>
/// Configuration for an agent, including its model, provider, and tools.
/// </summary>
public class AgentConfiguration
{
    /// <summary>
    /// The agent provider name (e.g., "github-copilot", "anthropic").
    /// </summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; set; }

    /// <summary>
    /// The model name to use (e.g., "claude-sonnet-4").
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// The reasoning effort level (e.g., "low", "medium", "high").
    /// </summary>
    [JsonPropertyName("reasoningEffort")]
    public string? ReasoningEffort { get; set; }

    /// <summary>
    /// List of agent tool source keys from the agentToolSources configuration to make available to the agent.
    /// </summary>
    [JsonPropertyName("tools")]
    public List<string> Tools { get; set; } = new();

    /// <summary>
    /// Path to a prompt file to use for this agent.
    /// </summary>
    [JsonPropertyName("promptFile")]
    public string? PromptFile { get; set; }
}
