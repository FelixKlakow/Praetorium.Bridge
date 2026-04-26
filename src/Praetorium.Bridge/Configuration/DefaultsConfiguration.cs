using System.Text.Json.Serialization;

namespace Praetorium.Bridge.Configuration;

/// <summary>
/// Default configuration values for agents, sessions, and signaling.
/// </summary>
public class DefaultsConfiguration
{
    /// <summary>
    /// Default agent configuration.
    /// </summary>
    [JsonPropertyName("agent")]
    public AgentConfiguration Agent { get; set; } = new();

    /// <summary>
    /// Default session configuration.
    /// </summary>
    [JsonPropertyName("session")]
    public SessionConfiguration Session { get; set; } = new();

    /// <summary>
    /// Default signaling configuration.
    /// </summary>
    [JsonPropertyName("signaling")]
    public SignalingConfiguration Signaling { get; set; } = new();

    /// <summary>
    /// Default system prompt applied to every tool's agent session unless the tool
    /// provides an override. Deliberately short — tool-specific detail belongs in
    /// the tool's own prompt file, not here.
    /// </summary>
    [JsonPropertyName("systemPrompt")]
    public SystemPromptConfiguration SystemPrompt { get; set; } = new()
    {
        Content = "You are a bridged agent. Respond exclusively by calling the signaling tools exposed on this session. Blocking tools wait for a caller reply; non-blocking tools stream data and return immediately.",
    };
}
