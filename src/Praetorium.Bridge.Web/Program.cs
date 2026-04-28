using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Praetorium.Bridge.Extensions;
using Praetorium.Bridge.CopilotProvider;
using Praetorium.Bridge.CopilotProvider.InternalMcp;
using Praetorium.Bridge.Hooks;
using Praetorium.Bridge.Mcp;
using Praetorium.Bridge.Signaling;
using Praetorium.Bridge.Web.Services;
using Praetorium.Bridge.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Reserve a free loopback TCP port for the internal MCP endpoint.
int internalMcpPort;
{
    var probe = new TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    internalMcpPort = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
}

// Append the loopback address to whatever URLs are already configured
// (ASPNETCORE_URLS / applicationUrl in launchSettings). Using UseSetting
// before Build() is additive: the host still listens on the public address
// from launchSettings AND on the internal loopback address. Using
// app.Urls.Add() after Build() or ConfigureKestrel(options.Listen(...))
// replaces the default addresses entirely.
var existingUrls = builder.Configuration["ASPNETCORE_URLS"]
    ?? builder.Configuration["urls"]
    ?? "http://localhost:5000";
builder.WebHost.UseSetting(
    WebHostDefaults.ServerUrlsKey,
    $"{existingUrls};http://127.0.0.1:{internalMcpPort}");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DetailedErrors = builder.Environment.IsDevelopment();
    });

builder.Services.Configure<HubOptions<DashboardHub>>(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

builder.Logging.AddFilter("Microsoft.AspNetCore.Components.Server.Circuits", LogLevel.Warning);

// Register the loopback MCP endpoint description so CopilotAgentProvider can
// resolve it. The port was chosen above and Kestrel is already configured to
// listen on it, so we can create the singleton immediately.
builder.Services.AddSingleton(new InternalMcpEndpoint(internalMcpPort));

// Register Praetorium.Bridge core services
builder.Services.AddPraetoriumBridge();
builder.Services.AddCopilotProvider();

// Register web services
builder.Services.AddSingleton<ConfigurationService>();
builder.Services.AddSingleton<DashboardBridgeHooks>();

// Per-session live transcript for the Sessions dashboard page. Singleton so it
// starts buffering bridge + signaling events as soon as the host comes up.
builder.Services.AddSingleton<SessionActivityService>();

// Config Agent: per-user (per Blazor circuit) scoped chat + staged changes.
builder.Services.AddScoped<Praetorium.Bridge.Web.Services.ConfigAgent.ConfigAgentService>();

// Override the default IBridgeHooks with DashboardBridgeHooks
builder.Services.AddSingleton<IBridgeHooks>(sp => sp.GetRequiredService<DashboardBridgeHooks>());

builder.Services.AddHttpContextAccessor();

// Register MCP server with dynamic tools from bridge configuration. A single
// IMcpServerBuilder registration serves both endpoints (the public /mcp and
// the loopback-only /mcp-int); handlers branch on HttpContext.Request.Path.
builder.Services.AddMcpServer(options =>
{
    options.ServerInfo = new() { Name = "Praetorium Bridge", Version = "0.1.0" };
})
.WithHttpTransport()
.WithListToolsHandler((context, ct) =>
{
    var services = context.Server.Services!;
    var httpContext = services.GetRequiredService<IHttpContextAccessor>().HttpContext;

    if (IsInternalRequest(httpContext))
    {
        var entry = GetInternalEntryOrThrow(httpContext);
        var tools = new List<Tool>(entry.Tools.Count);
        foreach (var t in entry.Tools)
        {
            tools.Add(new Tool { Name = t.Name, Description = t.Description, InputSchema = t.ParametersSchema });
        }
        return ValueTask.FromResult(new ListToolsResult { Tools = tools });
    }

    var mcpBuilder = services.GetRequiredService<McpServerBuilder>();
    var definitions = mcpBuilder.BuildToolDefinitions();

    // Register this server session so configuration-change notifications can
    // be broadcast to all currently-connected public MCP clients.
    var tracker = services.GetRequiredService<McpServerTracker>();
    tracker.Register(context.Server);

    var publicTools = definitions.Select(d => new Tool
    {
        Name = d.Name,
        Description = d.Description,
        InputSchema = d.InputSchema,
    }).ToList();

    return ValueTask.FromResult(new ListToolsResult { Tools = publicTools });
})
.WithCallToolHandler(async (context, ct) =>
{
    var services = context.Server.Services!;
    var httpContext = services.GetRequiredService<IHttpContextAccessor>().HttpContext;
    var toolName = context.Params.Name;
    var arguments = context.Params.Arguments is { } args
        ? JsonSerializer.SerializeToElement(args)
        : default;

    // Build a progress sink from the request's ProgressToken so blocking
    // handlers can emit periodic keepalives back to the MCP caller. When the
    // caller did not supply a progress token, notifications are silently
    // dropped by the null sink.
    IProgress<ProgressNotificationValue>? progress = null;
    var progressToken = context.Params.ProgressToken;
    if (progressToken is { } token)
    {
        var server = context.Server;
        progress = new Progress<ProgressNotificationValue>(value =>
        {
            _ = server.NotifyProgressAsync(token, value, options: default, ct);
        });
    }

    string json;
    if (IsInternalRequest(httpContext))
    {
        var entry = GetInternalEntryOrThrow(httpContext);
        var tool = entry.Tools.FirstOrDefault(t => t.Name == toolName)
            ?? throw new McpException(
                $"Tool '{toolName}' is not available on the internal MCP endpoint for this session.");

        // Internal callers (the in-process Copilot agent) bypass the outer
        // ToolDispatcher entirely: they target a concrete signaling tool
        // bound to their session, so we invoke the handler directly.
        json = await tool.Handler(arguments, progress, ct).ConfigureAwait(false);
    }
    else
    {
        var handler = services.GetRequiredService<DynamicToolHandler>();
        json = await handler.HandleToolCallAsync(toolName, arguments, connectionId: null, progress, ct).ConfigureAwait(false);
    }

    return new CallToolResult
    {
        Content = [new TextContentBlock { Text = json }]
    };
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapHub<DashboardHub>("/hubs/dashboard");
app.MapMcp("/mcp");

// Loopback-only internal MCP endpoint. The UseWhen branch short-circuits with
// 403/401/404 for any request that fails loopback/auth/session validation, so
// the inner MapMcp handlers only ever see authenticated loopback traffic.
const string internalMcpPath = InternalMcpEndpoint.Path;
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments(internalMcpPath),
    branch => branch.Use(async (ctx, next) =>
    {
        var remote = ctx.Connection.RemoteIpAddress;
        if (remote is null || !IPAddress.IsLoopback(remote))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsync("Forbidden");
            return;
        }

        var sessionKey = ctx.Request.Headers[InternalMcpEndpoint.SessionHeaderName].ToString();
        var bearer = ctx.Request.Headers[InternalMcpEndpoint.BearerTokenHeaderName].ToString();
        if (string.IsNullOrEmpty(sessionKey) || string.IsNullOrEmpty(bearer))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("Unauthorized");
            return;
        }

        var registry = ctx.RequestServices.GetRequiredService<IInternalMcpRegistry>();
        if (!registry.TryGet(sessionKey, out var entry)
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(bearer),
                System.Text.Encoding.UTF8.GetBytes(entry.BearerToken)))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("Unauthorized");
            return;
        }

        ctx.Items[InternalMcpRequestItem.Key] = entry;
        await next(ctx).ConfigureAwait(false);
    }));
app.MapMcp(internalMcpPath);

app.Run();

// Helpers shared by the MCP list/call handlers above.
static bool IsInternalRequest(HttpContext? httpContext)
    => httpContext is not null
       && httpContext.Request.Path.StartsWithSegments(InternalMcpEndpoint.Path);

static InternalMcpRegistryEntry GetInternalEntryOrThrow(HttpContext? httpContext)
{
    if (httpContext is null
        || !httpContext.Items.TryGetValue(InternalMcpRequestItem.Key, out var value)
        || value is not InternalMcpRegistryEntry entry)
    {
        throw new McpException(
            "Internal MCP request is missing the authenticated session entry.");
    }
    return entry;
}

internal static class InternalMcpRequestItem
{
    public const string Key = "Praetorium.InternalMcp.Entry";
}
