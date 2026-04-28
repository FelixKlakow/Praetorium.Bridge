using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Praetorium.Bridge.Mcp;

/// <summary>
/// Tracks active MCP server sessions so that server-to-client notifications
/// (e.g. <c>notifications/tools/list_changed</c>) can be broadcast to every
/// connected client when the bridge configuration changes.
/// </summary>
public class McpServerTracker
{
    private readonly ConcurrentDictionary<string, WeakReference<McpServer>> _servers = new();

    /// <summary>
    /// Registers a server session. Subsequent calls with the same
    /// <see cref="McpServer.SessionId"/> replace the previous entry.
    /// </summary>
    public void Register(McpServer server)
    {
        if (server == null)
            throw new ArgumentNullException(nameof(server));

        var key = server.SessionId
            ?? throw new InvalidOperationException("McpServer.SessionId must not be null.");
        _servers[key] = new WeakReference<McpServer>(server);
    }

    /// <summary>
    /// Sends <c>notifications/tools/list_changed</c> to every live server
    /// session that is still reachable. Dead (GC-collected) entries are pruned
    /// opportunistically during the send pass.
    /// </summary>
    public async Task SendToolListChangedAsync(CancellationToken ct = default)
    {
        foreach (var (key, weakRef) in _servers)
        {
            if (!weakRef.TryGetTarget(out var server))
            {
                _servers.TryRemove(key, out _);
                continue;
            }

            try
            {
                await server.SendNotificationAsync(
                    NotificationMethods.ToolListChangedNotification, ct)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The session may have been closed between the GC check and
                // the send; swallow the error to keep other sessions working.
                _servers.TryRemove(key, out _);
            }
        }
    }
}
