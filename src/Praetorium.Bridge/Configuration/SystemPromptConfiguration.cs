using System.Text.Json.Serialization;

namespace Praetorium.Bridge.Configuration;

/// <summary>
/// Short, static system prompt installed on the agent's session at creation time.
/// Deliberately not templated: the purpose is to describe tool-calling conventions
/// (respond via signaling tools, blocking vs non-blocking, etc.). The detailed
/// per-tool instructions live in the tool's own prompt template and are sent as
/// the user message on each fresh turn.
/// <para>
/// <see cref="Content"/> and <see cref="PromptFile"/> are mutually exclusive.
/// </para>
/// </summary>
public class SystemPromptConfiguration
{
    /// <summary>
    /// Inline system prompt text. Mutually exclusive with <see cref="PromptFile"/>.
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>
    /// Path to a file (relative to the config directory) whose contents are used
    /// verbatim as the system prompt. Mutually exclusive with <see cref="Content"/>.
    /// </summary>
    [JsonPropertyName("promptFile")]
    public string? PromptFile { get; set; }
}
