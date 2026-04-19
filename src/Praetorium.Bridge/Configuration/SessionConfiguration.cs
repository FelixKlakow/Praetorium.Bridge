using System.Text.Json.Serialization;

namespace Praetorium.Bridge.Configuration;

/// <summary>
/// Configuration for session behavior.
/// </summary>
public class SessionConfiguration
{
    /// <summary>
    /// The session mode determining how sessions are created and managed.
    /// </summary>
    [JsonPropertyName("mode")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SessionMode Mode { get; set; } = SessionMode.PerConnection;

    /// <summary>
    /// The parameter name used to identify the reference ID for session pooling.
    /// </summary>
    [JsonPropertyName("referenceIdParameter")]
    public string? ReferenceIdParameter { get; set; }

    /// <summary>
    /// The timeout in minutes for pooled sessions.
    /// </summary>
    [JsonPropertyName("poolTimeoutMinutes")]
    public int PoolTimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// The timeout in minutes for waiting on a response from the agent.
    /// </summary>
    [JsonPropertyName("responseTimeoutMinutes")]
    public int ResponseTimeoutMinutes { get; set; } = 10;

    /// <summary>
    /// The timeout in seconds for waiting on a response from the agent.
    /// </summary>
    [JsonPropertyName("responseTimeoutSeconds")]
    public int ResponseTimeoutSeconds { get; set; } = 600;
}
