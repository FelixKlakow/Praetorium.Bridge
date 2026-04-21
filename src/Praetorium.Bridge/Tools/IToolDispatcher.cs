using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol;

namespace Praetorium.Bridge.Tools;

/// <summary>
/// Interface for dispatching tool calls and managing their execution.
/// </summary>
public interface IToolDispatcher
{
    /// <summary>
    /// Dispatches a tool call to the appropriate agent session or creates a new one.
    /// External MCP callers are always blocking: the returned task only completes once a
    /// signaling tool on the agent side produces an outbound signal (or the turn fails).
    /// </summary>
    /// <param name="toolName">The name of the tool being invoked.</param>
    /// <param name="arguments">The arguments provided to the tool, as a JSON element.</param>
    /// <param name="connectionId">Optional connection ID for session mode determination.</param>
    /// <param name="progress">
    /// Optional progress sink used to emit periodic keepalive notifications to the external
    /// caller while the agent is working. When null, no notifications are emitted.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A ToolResponse containing the result of the tool invocation.</returns>
    Task<ToolResponse> DispatchAsync(
        string toolName,
        JsonElement arguments,
        string? connectionId,
        IProgress<ProgressNotificationValue>? progress,
        CancellationToken ct);
}

