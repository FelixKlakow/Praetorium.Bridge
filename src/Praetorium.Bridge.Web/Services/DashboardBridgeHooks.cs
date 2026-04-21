using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Praetorium.Bridge.Hooks;

namespace Praetorium.Bridge.Web.Services;

/// <summary>
/// Bridge hooks implementation that broadcasts events to the dashboard via SignalR
/// and raises in-process events for Blazor Server components to subscribe to directly.
/// </summary>
public class DashboardBridgeHooks : IBridgeHooks
{
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly List<ActivityLogEntry> _activityLog = new();
    private const int MaxLogSize = 1000;

    /// <summary>
    /// Raised when a new activity entry is logged.
    /// </summary>
    public event Func<ActivityLogEntry, Task>? ActivityLogged;

    /// <summary>
    /// Raised when the bridge configuration is reloaded.
    /// </summary>
    public event Func<Task>? ConfigReloaded;

    /// <summary>
    /// Raised when a session is spawned. Fires after the bridge's own spawn
    /// hook so the active agent is retrievable via <c>ISessionManager.GetActiveAgent</c>.
    /// Used by <see cref="SessionActivityService"/> to attach per-session observers.
    /// </summary>
    public event Func<SessionContext, Task>? SessionSpawned;

    /// <summary>
    /// Raised after any hook that carries a <c>SessionId</c>, projected into a
    /// flat <see cref="SessionLifecycleEntry"/>. Subscribed by
    /// <see cref="SessionActivityService"/> to drive the per-session dashboard view.
    /// </summary>
    public event Action<SessionLifecycleEntry>? SessionLifecycle;

    /// <summary>
    /// Initializes a new instance of the DashboardBridgeHooks class.
    /// </summary>
    /// <param name="hubContext">The SignalR hub context for broadcasting updates.</param>
    public DashboardBridgeHooks(IHubContext<DashboardHub> hubContext)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
    }

    private async Task RaiseActivityLoggedAsync(ActivityLogEntry entry, CancellationToken ct)
    {
        await _hubContext.Clients.All.SendAsync("ActivityLogged", entry, cancellationToken: ct);

        var handler = ActivityLogged;
        if (handler is null)
            return;

        foreach (Func<ActivityLogEntry, Task> subscriber in handler.GetInvocationList().Cast<Func<ActivityLogEntry, Task>>())
        {
            try
            {
                await subscriber(entry);
            }
            catch
            {
                // Do not let a single subscriber break the hook pipeline.
            }
        }
    }

    private async Task RaiseConfigReloadedAsync(CancellationToken ct)
    {
        await _hubContext.Clients.All.SendAsync("ConfigReloaded", cancellationToken: ct);

        var handler = ConfigReloaded;
        if (handler is null)
            return;

        foreach (Func<Task> subscriber in handler.GetInvocationList().Cast<Func<Task>>())
        {
            try
            {
                await subscriber();
            }
            catch
            {
                // Do not let a single subscriber break the hook pipeline.
            }
        }
    }

    /// <summary>
    /// Called when a tool is invoked by a client.
    /// </summary>
    public async Task OnToolInvokedAsync(ToolInvocationContext context, CancellationToken ct)
    {
        var parameterNames = string.Join(", ", context.Parameters.Keys);
        var logEntry = new ActivityLogEntry(
            DateTimeOffset.UtcNow,
            context.ToolName,
            context.ConnectionId,
            "Tool Invoked",
            $"Parameters: {parameterNames}");

        AddLogEntry(logEntry);
        await RaiseActivityLoggedAsync(logEntry, ct);
    }

    /// <summary>
    /// Called when a new session is spawned.
    /// </summary>
    public async Task OnSessionSpawnedAsync(SessionContext context, CancellationToken ct)
    {
        var logEntry = new ActivityLogEntry(
            DateTimeOffset.UtcNow,
            context.ToolName,
            context.ReferenceId,
            "Session Spawned",
            $"Session ID: {context.SessionId}");

        AddLogEntry(logEntry);
        RaiseSessionLifecycle(context.SessionId, "Spawned", context.ToolName, $"Reference: {context.ReferenceId ?? "-"}");

        if (!string.IsNullOrWhiteSpace(context.Prompt))
        {
            RaiseSessionLifecycle(context.SessionId, "Prompt", context.ToolName, context.Prompt);
        }

        await RaiseActivityLoggedAsync(logEntry, ct);
        await RaiseSessionSpawnedAsync(context);
    }

    private async Task RaiseSessionSpawnedAsync(SessionContext context)
    {
        var handler = SessionSpawned;
        if (handler is null)
            return;

        foreach (Func<SessionContext, Task> subscriber in handler.GetInvocationList().Cast<Func<SessionContext, Task>>())
        {
            try
            {
                await subscriber(context);
            }
            catch
            {
                // Do not let a single subscriber break the hook pipeline.
            }
        }
    }

    private void RaiseSessionLifecycle(string sessionId, string kind, string? toolName, string? details)
    {
        var handler = SessionLifecycle;
        if (handler is null) return;

        var entry = new SessionLifecycleEntry(
            DateTimeOffset.UtcNow,
            sessionId,
            kind,
            toolName,
            details);

        try { handler(entry); }
        catch { /* observers must not break the hook pipeline */ }
    }

    /// <summary>
    /// Called when a session is retrieved from the pool.
    /// </summary>
    public async Task OnSessionPooledAsync(SessionContext context, CancellationToken ct)
    {
        var logEntry = new ActivityLogEntry(
            DateTimeOffset.UtcNow,
            context.ToolName,
            context.ReferenceId,
            "Session Pooled",
            $"Session ID: {context.SessionId}");

        AddLogEntry(logEntry);
        RaiseSessionLifecycle(context.SessionId, "Pooled", context.ToolName, null);
        await RaiseActivityLoggedAsync(logEntry, ct);
    }

    /// <summary>
    /// Called when a pooled session is reactivated.
    /// </summary>
    public async Task OnSessionWokenAsync(SessionContext context, CancellationToken ct)
    {
        var logEntry = new ActivityLogEntry(
            DateTimeOffset.UtcNow,
            context.ToolName,
            context.ReferenceId,
            "Session Woken",
            $"Session ID: {context.SessionId}");

        AddLogEntry(logEntry);
        RaiseSessionLifecycle(context.SessionId, "Woken", context.ToolName, null);
        await RaiseActivityLoggedAsync(logEntry, ct);
    }

    /// <summary>
    /// Called when a session is dropped.
    /// </summary>
    public async Task OnSessionDroppedAsync(SessionDroppedContext context, CancellationToken ct)
    {
        var logEntry = new ActivityLogEntry(
            DateTimeOffset.UtcNow,
            context.ToolName,
            context.ReferenceId,
            "Session Dropped",
            $"Reason: {context.Reason}");

        AddLogEntry(logEntry);
        RaiseSessionLifecycle(context.SessionId, "Dropped", context.ToolName, $"Reason: {context.Reason}");
        await RaiseActivityLoggedAsync(logEntry, ct);
    }

    /// <summary>
    /// Called when an agent crashes unexpectedly.
    /// </summary>
    public async Task OnAgentCrashedAsync(AgentCrashedContext context, CancellationToken ct)
    {
        var logEntry = new ActivityLogEntry(
            DateTimeOffset.UtcNow,
            context.ToolName,
            context.ReferenceId,
            "Agent Crashed",
            context.Exception.Message);

        AddLogEntry(logEntry);
        RaiseSessionLifecycle(context.SessionId, "Crashed", context.ToolName, context.Exception.Message);
        await RaiseActivityLoggedAsync(logEntry, ct);
    }

    /// <summary>
    /// Called when a response is delivered to a client.
    /// </summary>
    public async Task OnResponseDeliveredAsync(ResponseContext context, CancellationToken ct)
    {
        var logEntry = new ActivityLogEntry(
            DateTimeOffset.UtcNow,
            context.ToolName,
            context.SessionId,
            "Response Delivered",
            $"Duration: {context.DurationMs}ms, Message: {context.Message}");

        AddLogEntry(logEntry);
        RaiseSessionLifecycle(context.SessionId, "Response Delivered", context.ToolName, $"{context.DurationMs}ms: {context.Message}");
        await RaiseActivityLoggedAsync(logEntry, ct);
    }

    /// <summary>
    /// Called when the agent requests input from the user.
    /// </summary>
    public async Task OnInputRequestedAsync(InputRequestContext context, CancellationToken ct)
    {
        var logEntry = new ActivityLogEntry(
            DateTimeOffset.UtcNow,
            context.ToolName,
            context.SessionId,
            "Input Requested",
            context.Question);

        AddLogEntry(logEntry);
        RaiseSessionLifecycle(context.SessionId, "Input Requested", context.ToolName, context.Question);
        await RaiseActivityLoggedAsync(logEntry, ct);
    }

    /// <summary>
    /// Called when input is received from a user.
    /// </summary>
    public async Task OnInputReceivedAsync(InputReceivedContext context, CancellationToken ct)
    {
        var logEntry = new ActivityLogEntry(
            DateTimeOffset.UtcNow,
            context.ToolName,
            context.SessionId,
            "Input Received",
            $"Input: {context.Input}");

        AddLogEntry(logEntry);
        RaiseSessionLifecycle(context.SessionId, "Input Received", context.ToolName, context.Input);
        await RaiseActivityLoggedAsync(logEntry, ct);
    }

    /// <summary>
    /// Called when a client disconnects from the bridge.
    /// </summary>
    public async Task OnCallerDisconnectedAsync(CallerDisconnectedContext context, CancellationToken ct)
    {
        var logEntry = new ActivityLogEntry(
            DateTimeOffset.UtcNow,
            "Bridge",
            context.ConnectionId,
            "Caller Disconnected",
            null);

        AddLogEntry(logEntry);
        await RaiseActivityLoggedAsync(logEntry, ct);
    }

    /// <summary>
    /// Called when the bridge configuration is reloaded.
    /// </summary>
    public async Task OnConfigReloadedAsync(ConfigReloadedContext context, CancellationToken ct)
    {
        var logEntry = new ActivityLogEntry(
            DateTimeOffset.UtcNow,
            "Bridge",
            null,
            "Configuration Reloaded",
            null);

        AddLogEntry(logEntry);
        await RaiseConfigReloadedAsync(ct);
    }

    /// <summary>
    /// Adds an entry to the activity log, maintaining a bounded size.
    /// </summary>
    private void AddLogEntry(ActivityLogEntry entry)
    {
        lock (_activityLog)
        {
            _activityLog.Add(entry);
            if (_activityLog.Count > MaxLogSize)
            {
                _activityLog.RemoveRange(0, _activityLog.Count - MaxLogSize);
            }
        }
    }

    /// <summary>
    /// Gets the recent activity log entries.
    /// </summary>
    /// <param name="count">The number of recent entries to return.</param>
    /// <returns>A list of recent activity log entries.</returns>
    public List<ActivityLogEntry> GetRecentActivity(int count = 100)
    {
        lock (_activityLog)
        {
            return _activityLog.TakeLast(count).ToList();
        }
    }
}
