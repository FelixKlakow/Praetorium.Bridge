using System.Text.Json;

namespace Praetorium.Bridge.Mcp;

/// <summary>
/// Internal representation of a tool definition compatible with the MCP protocol.
/// </summary>
public class McpToolDefinition
{
    /// <summary>
    /// Initializes a new instance of the McpToolDefinition class.
    /// </summary>
    /// <param name="name">The name of the tool.</param>
    /// <param name="description">A description of what the tool does.</param>
    /// <param name="inputSchema">The JSON Schema for the tool's input parameters.</param>
    public McpToolDefinition(string name, string? description, JsonElement inputSchema)
    {
        Name = name;
        Description = description;
        InputSchema = inputSchema;
    }

    /// <summary>
    /// Gets the name of the tool.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets a description of what the tool does.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Gets the JSON Schema for the tool's input parameters.
    /// </summary>
    public JsonElement InputSchema { get; }
}
