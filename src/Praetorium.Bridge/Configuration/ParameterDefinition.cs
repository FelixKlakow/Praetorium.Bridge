using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Praetorium.Bridge.Configuration;

/// <summary>
/// Definition of a parameter for a tool.
/// </summary>
public class ParameterDefinition
{
    /// <summary>
    /// The JSON schema type of the parameter (e.g., "string", "number", "object", "array").
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// A description of the parameter.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Whether this parameter is required. Enforcement depends on the parameter's
    /// <see cref="Kind"/> and the current dispatch <see cref="Tools.TurnPhase"/>:
    /// <see cref="ParameterKind.Prompt"/> is only required on a fresh turn,
    /// <see cref="ParameterKind.Resume"/> only when resuming a blocked session,
    /// <see cref="ParameterKind.System"/> is never caller-required.
    /// </summary>
    [JsonPropertyName("required")]
    public bool Required { get; set; }

    /// <summary>
    /// Classification controlling when <see cref="Required"/> is enforced. Defaults
    /// to <see cref="ParameterKind.Prompt"/>.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ParameterKind Kind { get; set; } = ParameterKind.Prompt;

    /// <summary>
    /// For array types, the schema of items in the array.
    /// </summary>
    [JsonPropertyName("items")]
    public ParameterDefinition? Items { get; set; }

    /// <summary>
    /// For object types, the properties of the object.
    /// </summary>
    [JsonPropertyName("properties")]
    public Dictionary<string, ParameterDefinition>? Properties { get; set; }
}
