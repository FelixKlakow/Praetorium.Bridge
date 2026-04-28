using System;
using System.Collections.Generic;
using System.Text.Json;
using Praetorium.Bridge.Configuration;
using Praetorium.Bridge.Tools;

namespace Praetorium.Bridge.Mcp;

/// <summary>
/// Builds MCP server configuration from bridge configuration, including tool definitions and JSON schemas.
/// </summary>
public class McpServerBuilder
{
    private readonly IConfigurationProvider _configurationProvider;
    private readonly IToolDispatcher _toolDispatcher;
    private readonly McpServerTracker _serverTracker;
    private McpToolDefinition[]? _cachedToolDefinitions;

    /// <summary>
    /// Initializes a new instance of the McpServerBuilder class.
    /// </summary>
    /// <param name="configurationProvider">The bridge configuration provider.</param>
    /// <param name="toolDispatcher">The tool dispatcher for executing tool calls.</param>
    /// <param name="serverTracker">Tracker used to broadcast tool-list-changed notifications.</param>
    public McpServerBuilder(IConfigurationProvider configurationProvider, IToolDispatcher toolDispatcher, McpServerTracker serverTracker)
    {
        _configurationProvider = configurationProvider ?? throw new ArgumentNullException(nameof(configurationProvider));
        _toolDispatcher = toolDispatcher ?? throw new ArgumentNullException(nameof(toolDispatcher));
        _serverTracker = serverTracker ?? throw new ArgumentNullException(nameof(serverTracker));

        // Subscribe to configuration changes
        RebuildOnConfigChange();
    }

    /// <summary>
    /// Builds tool definitions from the current configuration.
    /// </summary>
    /// <returns>An array of MCP-compatible tool definitions.</returns>
    public McpToolDefinition[] BuildToolDefinitions()
    {
        if (_cachedToolDefinitions != null)
            return _cachedToolDefinitions;

        var config = _configurationProvider.Configuration;
        var toolDefinitions = new List<McpToolDefinition>();

        foreach (var (toolName, toolDef) in config.Tools)
        {
            var toolDefForMcp = BuildMcpToolDefinition(toolName, toolDef);
            toolDefinitions.Add(toolDefForMcp);
        }

        _cachedToolDefinitions = toolDefinitions.ToArray();
        return _cachedToolDefinitions;
    }

    /// <summary>
    /// Subscribes to configuration changes, rebuilds tool definitions on reload,
    /// and notifies connected MCP clients that the tool list has changed.
    /// </summary>
    public void RebuildOnConfigChange()
    {
        _configurationProvider.OnConfigurationChanged += config =>
        {
            _cachedToolDefinitions = null;
            // Fire-and-forget: sending the notification is best-effort because
            // individual sessions may have already disconnected. The
            // ContinueWith observes any unexpected task faults so they are not
            // silently swallowed by the runtime.
            _ = _serverTracker.SendToolListChangedAsync()
                .ContinueWith(
                    t => System.Diagnostics.Debug.WriteLine(
                        $"Failed to send tools/list_changed notification: {t.Exception}"),
                    System.Threading.CancellationToken.None,
                    System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted,
                    System.Threading.Tasks.TaskScheduler.Default);
        };
    }

    /// <summary>
    /// Builds a single MCP tool definition from a tool definition.
    /// </summary>
    private McpToolDefinition BuildMcpToolDefinition(string toolName, ToolDefinition toolDef)
    {
        var description = toolDef.Description ?? $"Tool: {toolName}";
        var inputSchema = BuildInputSchema(toolDef);

        return new McpToolDefinition(toolName, description, inputSchema);
    }

    /// <summary>
    /// Builds the JSON Schema for a tool's input parameters.
    /// </summary>
    private JsonElement BuildInputSchema(ToolDefinition toolDef)
    {
        var schema = new Dictionary<string, object>
        {
            { "type", "object" },
            { "properties", BuildPropertySchemas(toolDef) },
            { "required", BuildRequiredList(toolDef) }
        };

        var jsonString = JsonSerializer.Serialize(schema);
        return JsonDocument.Parse(jsonString).RootElement;
    }

    /// <summary>
    /// Builds the properties section of the JSON Schema.
    /// </summary>
    private object BuildPropertySchemas(ToolDefinition toolDef)
    {
        var properties = new Dictionary<string, object>();

        // Add regular parameters
        if (toolDef.Parameters != null)
        {
            foreach (var (paramName, paramDef) in toolDef.Parameters)
            {
                properties[paramName] = BuildParameterSchema(paramDef);
            }
        }

        // Add reserved parameters
        properties[ReservedParameters.ResetSession] = new
        {
            type = "boolean",
            description = "Reset the session before executing this tool"
        };

        properties[ReservedParameters.Input] = new
        {
            type = "string",
            description = "Provide input in response to a tool input request"
        };

        properties[ReservedParameters.ReferenceId] = new
        {
            type = "string",
            description = "Reference ID for PerReference session mode"
        };

        return properties;
    }

    /// <summary>
    /// Builds a schema for a single parameter.
    /// </summary>
    private object BuildParameterSchema(ParameterDefinition paramDef)
    {
        var schema = new Dictionary<string, object>();

        if (!string.IsNullOrEmpty(paramDef.Type))
            schema["type"] = paramDef.Type;

        if (!string.IsNullOrEmpty(paramDef.Description))
            schema["description"] = paramDef.Description;

        if (paramDef.Items != null)
            schema["items"] = BuildParameterSchema(paramDef.Items);

        if (paramDef.Properties != null)
        {
            var props = new Dictionary<string, object>();
            foreach (var (propName, propDef) in paramDef.Properties)
            {
                props[propName] = BuildParameterSchema(propDef);
            }
            schema["properties"] = props;
        }

        return schema;
    }

    /// <summary>
    /// Builds the list of required parameters.
    /// </summary>
    private List<string> BuildRequiredList(ToolDefinition toolDef)
    {
        var required = new List<string>();

        if (toolDef.Parameters != null)
        {
            foreach (var (paramName, paramDef) in toolDef.Parameters)
            {
                if (paramDef.Required)
                {
                    required.Add(paramName);
                }
            }
        }

        return required;
    }
}
