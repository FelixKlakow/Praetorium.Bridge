using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<McpServerTracker> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerTracker"/> class.
    /// </summary>
    public McpServerTracker(ILogger<McpServerTracker>? logger = null)
    {
        _logger = logger ?? NullLogger<McpServerTracker>.Instance;
    }

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
        if (_servers.IsEmpty)
        {
            _logger.LogWarning(
                "tools/list_changed not delivered: no MCP sessions are tracked. " +
                "A client must call tools/list at least once before changes are broadcast.");
            return;
        }

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
            catch (Exception ex)
            {
                // The session may have been closed between the GC check and
                // the send; remove it and keep other sessions working.
                _servers.TryRemove(key, out _);
                _logger.LogWarning(
                    ex,
                    "Failed to send tools/list_changed to session {SessionId}; pruned from tracker.",
                    key);
            }
        }
    }
}
