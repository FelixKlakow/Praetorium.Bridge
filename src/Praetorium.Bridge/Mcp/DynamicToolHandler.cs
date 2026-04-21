using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;
using Praetorium.Bridge.Tools;

namespace Praetorium.Bridge.Mcp;

/// <summary>
/// Handles MCP tool calls by delegating to the tool dispatcher and serializing responses.
/// </summary>
public class DynamicToolHandler
{
    private readonly IToolDispatcher _dispatcher;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the DynamicToolHandler class.
    /// </summary>
    /// <param name="dispatcher">The tool dispatcher to delegate calls to.</param>
    public DynamicToolHandler(IToolDispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Handles a tool call from MCP by invoking the dispatcher and serializing the result.
    /// </summary>
    /// <param name="toolName">The name of the tool being invoked.</param>
    /// <param name="arguments">The arguments provided to the tool.</param>
    /// <param name="connectionId">Optional connection ID for the caller.</param>
    /// <param name="progress">
    /// Optional progress sink used by the dispatcher to emit periodic keepalive
    /// notifications to the external caller while the agent is working.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A JSON string representation of the tool response.</returns>
    public async Task<string> HandleToolCallAsync(
        string toolName,
        JsonElement arguments,
        string? connectionId,
        IProgress<ProgressNotificationValue>? progress,
        CancellationToken ct)
    {
        if (toolName == null)
            throw new ArgumentNullException(nameof(toolName));

        // Dispatch to the tool dispatcher
        var response = await _dispatcher.DispatchAsync(toolName, arguments, connectionId, progress, ct);

        // Serialize the response to JSON
        var json = JsonSerializer.Serialize(response, _jsonOptions);
        return json;
    }
}

