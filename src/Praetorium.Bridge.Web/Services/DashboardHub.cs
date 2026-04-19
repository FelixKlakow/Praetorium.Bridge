using System;
using Microsoft.AspNetCore.SignalR;
using Praetorium.Bridge.Sessions;

namespace Praetorium.Bridge.Web.Services;

/// <summary>
/// ActivityLogEntry represents a single activity logged during a bridge session.
/// </summary>
public record ActivityLogEntry(
    DateTimeOffset Timestamp,
    string ToolName,
    string? ReferenceId,
    string Event,
    string? Details);

/// <summary>
/// SignalR hub for broadcasting live dashboard updates to connected clients.
/// </summary>
public class DashboardHub : Hub
{
    /// <summary>
    /// Notifies clients that a session has been updated.
    /// </summary>
    /// <param name="session">The updated session information.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task SessionUpdatedAsync(SessionInfo session)
    {
        await Clients.All.SendAsync("SessionUpdated", session);
    }

    /// <summary>
    /// Notifies clients that an activity has been logged.
    /// </summary>
    /// <param name="entry">The activity log entry.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ActivityLoggedAsync(ActivityLogEntry entry)
    {
        await Clients.All.SendAsync("ActivityLogged", entry);
    }

    /// <summary>
    /// Notifies clients that the configuration has been reloaded.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ConfigReloadedAsync()
    {
        await Clients.All.SendAsync("ConfigReloaded");
    }
}
