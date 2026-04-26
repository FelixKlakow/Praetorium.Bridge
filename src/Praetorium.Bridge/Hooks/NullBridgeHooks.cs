using System.Threading;
using System.Threading.Tasks;

namespace Praetorium.Bridge.Hooks;

/// <summary>
/// A no-op implementation of IBridgeHooks that does nothing for all hook methods.
/// Useful as a default implementation when hooks are not needed.
/// </summary>
public class NullBridgeHooks : IBridgeHooks
{
    /// <inheritdoc />
    public Task OnToolInvokedAsync(ToolInvocationContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnSessionSpawnedAsync(SessionContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnSessionPooledAsync(SessionContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnSessionWokenAsync(SessionContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnSessionDroppedAsync(SessionDroppedContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnAgentCrashedAsync(AgentCrashedContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnResponseDeliveredAsync(ResponseContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnInputRequestedAsync(InputRequestContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnInputReceivedAsync(InputReceivedContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnCallerDisconnectedAsync(CallerDisconnectedContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnConfigReloadedAsync(ConfigReloadedContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnTurnStartedAsync(TurnStartedContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnTurnEndedAsync(TurnEndedContext context, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
