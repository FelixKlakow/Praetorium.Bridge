using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace Praetorium.Bridge.Tests.Mcp;

/// <summary>
/// End-to-end test that proves the ModelContextProtocol 1.2.0 SDK delivers
/// <c>notifications/tools/list_changed</c> from server to client over the
/// Streamable HTTP transport when the server-side tool collection changes.
///
/// This isolates the SDK's notification pipeline from anything Praetorium-
/// specific. If this test passes, the SDK is fine and any failure to refresh
/// in Visual Studio is a client-side / GET-stream issue. If this test fails
/// against SDK 1.2.0, we have a reproducible bug at the SDK / transport level.
/// </summary>
public sealed class ToolsListChangedNotificationTests : IAsyncDisposable
{
    private readonly McpServerPrimitiveCollection<McpServerTool> _tools = [];
    private WebApplication? _app;
    private string _baseAddress = "";

    [Fact]
    public async Task Server_collection_change_delivers_tools_list_changed_to_client()
    {
        // Arrange — start a minimal MCP server with tools.listChanged advertised.
        await StartServerAsync();

        // Initial tool so the first ListToolsAsync returns something deterministic.
        _tools.Add(McpServerTool.Create(
            (string input) => $"echo:{input}",
            new McpServerToolCreateOptions { Name = "echo" }));

        var notificationReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var notificationCount = 0;

        var clientOptions = new McpClientOptions
        {
            ClientInfo = new() { Name = "PraetoriumTestClient", Version = "0.0.1" },
            Handlers = new McpClientHandlers
            {
                NotificationHandlers =
                [
                    new KeyValuePair<string, Func<JsonRpcNotification, CancellationToken, ValueTask>>(
                        NotificationMethods.ToolListChangedNotification,
                        (_, _) =>
                        {
                            Interlocked.Increment(ref notificationCount);
                            notificationReceived.TrySetResult(true);
                            return ValueTask.CompletedTask;
                        })
                ],
            },
        };

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(_baseAddress + "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            Name = "PraetoriumTest",
        });

        await using var client = await McpClient.CreateAsync(transport, clientOptions);

        // Act — verify the server advertised the capability, list tools to open
        // the GET SSE stream, then mutate the tool collection on the server.
        Assert.True(client.ServerCapabilities.Tools?.ListChanged,
            "Server did not advertise tools.listChanged in its capabilities.");

        var initialTools = await client.ListToolsAsync();
        Assert.Single(initialTools);
        Assert.Equal("echo", initialTools[0].Name);

        // Add a second tool — the SDK's auto-wired Changed handler on
        // McpServerPrimitiveCollection<McpServerTool> should emit
        // notifications/tools/list_changed to the client.
        _tools.Add(McpServerTool.Create(
            (int x, int y) => x + y,
            new McpServerToolCreateOptions { Name = "add" }));

        // Assert — wait up to 5s for the notification.
        var completed = await Task.WhenAny(
            notificationReceived.Task,
            Task.Delay(TimeSpan.FromSeconds(5)));

        Assert.True(
            ReferenceEquals(completed, notificationReceived.Task),
            "Did not receive notifications/tools/list_changed within 5 seconds.");

        Assert.Equal(1, Volatile.Read(ref notificationCount));

        // Sanity check — the new tool is now visible.
        var afterTools = await client.ListToolsAsync();
        Assert.Equal(2, afterTools.Count);
    }

    private async Task StartServerAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new() { Name = "TestServer", Version = "0.0.1" };
                options.Capabilities ??= new ServerCapabilities();
                options.Capabilities.Tools ??= new ToolsCapability();
                options.Capabilities.Tools.ListChanged = true;

                // Hand the server the same primitive collection the test will
                // mutate. The SDK wires a Changed handler on this collection
                // that auto-emits notifications/tools/list_changed.
                options.ToolCollection = _tools;
            })
            .WithHttpTransport();

        _app = builder.Build();
        _app.MapMcp("/mcp");
        await _app.StartAsync();

        var server = _app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        var feature = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Server addresses feature missing.");
        _baseAddress = System.Linq.Enumerable.First(feature.Addresses);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
