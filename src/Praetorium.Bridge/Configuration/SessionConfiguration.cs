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
    /// A value of 0 or less means no timeout (wait indefinitely).
    /// </summary>
    [JsonPropertyName("responseTimeoutSeconds")]
    public int ResponseTimeoutSeconds { get; set; } = 0;

    /// <summary>
    /// When <c>true</c>, every <see cref="ParameterKind.Prompt"/> parameter is
    /// also treated as resume payload on a running turn — regardless of
    /// <see cref="Mode"/>. Use this on tools whose Prompt parameters always
    /// double as follow-up input (e.g. a review tool whose <c>context</c>
    /// parameter starts the review and also delivers follow-up notes).
    /// In <see cref="SessionMode.PerConnection"/> this behavior is the
    /// implicit default; in <see cref="SessionMode.PerReference"/> and
    /// <see cref="SessionMode.Global"/> it must be opted into here because
    /// those modes share the session across callers.
    /// </summary>
    [JsonPropertyName("treatPromptAsResume")]
    public bool TreatPromptAsResume { get; set; } = false;
}
