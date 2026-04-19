namespace Praetorium.Bridge.Agents;

/// <summary>
/// Describes the capabilities of an agent model, queried from the provider/SDK at runtime.
/// </summary>
public record AgentCapabilities
{
    /// <summary>
    /// Gets the name of the model.
    /// </summary>
    public required string ModelName { get; init; }

    /// <summary>
    /// Gets a value indicating whether the model supports reasoning effort configuration.
    /// </summary>
    public bool SupportsReasoningEffort { get; init; }

    /// <summary>
    /// Gets the supported reasoning effort levels for this model (e.g., "low", "medium", "high").
    /// Empty if reasoning effort is not supported.
    /// </summary>
    public IReadOnlyList<string> SupportedReasoningLevels { get; init; } = [];

    /// <summary>
    /// Gets the maximum number of tokens supported by the model, or null if unlimited.
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Gets a value indicating whether the model supports tool/function calling.
    /// </summary>
    public bool SupportsToolCalling { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the model supports streaming responses.
    /// </summary>
    public bool SupportsStreaming { get; init; } = true;

    /// <summary>
    /// Gets the provider-specific metadata about this model's capabilities.
    /// </summary>
    public IReadOnlyDictionary<string, object>? ProviderMetadata { get; init; }
}
