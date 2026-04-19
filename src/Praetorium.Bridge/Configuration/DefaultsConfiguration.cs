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
}
