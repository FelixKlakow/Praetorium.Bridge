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
    /// Whether this parameter is required.
    /// </summary>
    [JsonPropertyName("required")]
    public bool Required { get; set; }

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
