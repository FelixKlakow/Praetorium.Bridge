using System.Text.Json.Serialization;

namespace Praetorium.Bridge.Configuration;

/// <summary>
/// Configuration for prompt files.
/// </summary>
public class PromptConfiguration
{
    /// <summary>
    /// Path to a prompt file.
    /// </summary>
    [JsonPropertyName("promptFile")]
    public string? PromptFile { get; set; }
}
