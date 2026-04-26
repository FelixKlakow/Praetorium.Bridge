using System.Threading;
using System.Threading.Tasks;

namespace Praetorium.Bridge.Hooks;

/// <summary>
/// Interface for bridge hooks that allow external code to observe and react to bridge events.
/// All hook methods are called in order and must complete before the bridge continues processing.
/// </summary>
public interface IBridgeHooks
{
    /// <summary>
    /// Called when a tool is invoked by a client.
    /// </summary>
    /// <param name="context">The tool invocation context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnToolInvokedAsync(ToolInvocationContext context, CancellationToken ct);

    /// <summary>
    /// Called when a new session is spawned.
    /// </summary>
    /// <param name="context">The session context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnSessionSpawnedAsync(SessionContext context, CancellationToken ct);

    /// <summary>
    /// Called when a session is retrieved from the pool.
    /// </summary>
    /// <param name="context">The session context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnSessionPooledAsync(SessionContext context, CancellationToken ct);

    /// <summary>
    /// Called when a pooled session is reactivated.
    /// </summary>
    /// <param name="context">The session context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnSessionWokenAsync(SessionContext context, CancellationToken ct);

    /// <summary>
    /// Called when a session is dropped.
    /// </summary>
    /// <param name="context">The session dropped context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnSessionDroppedAsync(SessionDroppedContext context, CancellationToken ct);

    /// <summary>
    /// Called when an agent crashes unexpectedly.
    /// </summary>
    /// <param name="context">The agent crashed context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnAgentCrashedAsync(AgentCrashedContext context, CancellationToken ct);

    /// <summary>
    /// Called when a response is delivered to a client.
    /// </summary>
    /// <param name="context">The response context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnResponseDeliveredAsync(ResponseContext context, CancellationToken ct);

    /// <summary>
    /// Called when the agent requests input from the user.
    /// </summary>
    /// <param name="context">The input request context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnInputRequestedAsync(InputRequestContext context, CancellationToken ct);

    /// <summary>
    /// Called when input is received from a user.
    /// </summary>
    /// <param name="context">The input received context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnInputReceivedAsync(InputReceivedContext context, CancellationToken ct);

    /// <summary>
    /// Called when a client disconnects from the bridge.
    /// </summary>
    /// <param name="context">The caller disconnected context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnCallerDisconnectedAsync(CallerDisconnectedContext context, CancellationToken ct);

    /// <summary>
    /// Called when the bridge configuration is reloaded.
    /// </summary>
    /// <param name="context">The configuration reloaded context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task OnConfigReloadedAsync(ConfigReloadedContext context, CancellationToken ct);

    /// <summary>
    /// Called immediately before the dispatcher invokes <c>SendAsync</c> on the
    /// agent for a new turn. Carries the rendered per-turn tool prompt so observers
    /// can show the actual instructions handed to the agent.
    /// </summary>
    Task OnTurnStartedAsync(TurnStartedContext context, CancellationToken ct);

    /// <summary>
    /// Called when the agent turn task completes (cleanly, faulted, or cancelled).
    /// </summary>
    Task OnTurnEndedAsync(TurnEndedContext context, CancellationToken ct);
}
