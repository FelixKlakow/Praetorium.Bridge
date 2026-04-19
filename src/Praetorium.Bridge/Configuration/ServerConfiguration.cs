using System.Text.Json.Serialization;

namespace Praetorium.Bridge.Configuration;

/// <summary>
/// Configuration for the bridge server.
/// </summary>
public class ServerConfiguration
{
    /// <summary>
    /// The port the server listens on.
    /// </summary>
    [JsonPropertyName("port")]
    public int Port { get; set; } = 5100;

    /// <summary>
    /// The base path for MCP endpoints.
    /// </summary>
    [JsonPropertyName("basePath")]
    public string BasePath { get; set; } = "/mcp";

    /// <summary>
    /// The address the server binds to.
    /// </summary>
    [JsonPropertyName("bindAddress")]
    public string BindAddress { get; set; } = "localhost";
}
