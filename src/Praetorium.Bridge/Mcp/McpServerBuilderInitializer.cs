using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace Praetorium.Bridge.Mcp;

/// <summary>
/// Forces eager construction of <see cref="McpServerBuilder"/> at host start so
/// its subscription to <c>IConfigurationProvider.OnConfigurationChanged</c> is
/// established before any save can occur. Without this, the singleton would
/// only be created on the first <c>tools/list</c> request and configuration
/// changes prior to that point would not produce <c>tools/list_changed</c>
/// notifications.
/// </summary>
internal sealed class McpServerBuilderInitializer : IHostedService
{
    public McpServerBuilderInitializer(McpServerBuilder builder)
    {
        // Resolving the constructor parameter is sufficient — McpServerBuilder's
        // ctor wires the OnConfigurationChanged handler.
        _ = builder;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
