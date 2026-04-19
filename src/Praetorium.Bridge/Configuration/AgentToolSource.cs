using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Praetorium.Bridge.Configuration;

/// <summary>
/// Configuration for an MCP tool source that provides tools to an agent.
/// </summary>
public class AgentToolSource
{
    /// <summary>
    /// The type of tool source ("stdio" or "http").
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// The command to execute for stdio-based sources.
    /// </summary>
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    /// <summary>
    /// Arguments to pass to the command for stdio-based sources.
    /// </summary>
    [JsonPropertyName("args")]
    public string[]? Args { get; set; }

    /// <summary>
    /// The URL for http-based sources.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>
    /// HTTP headers to include when connecting to http-based sources.
    /// </summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = new();
}
