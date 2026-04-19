using Microsoft.AspNetCore.SignalR;
using Praetorium.Bridge.Extensions;
using Praetorium.Bridge.CopilotProvider;
using Praetorium.Bridge.Hooks;
using Praetorium.Bridge.Web.Services;
using Praetorium.Bridge.Web.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DetailedErrors = builder.Environment.IsDevelopment();
    });

builder.Services.Configure<HubOptions<DashboardHub>>(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

builder.Logging.AddFilter("Microsoft.AspNetCore.Components.Server.Circuits", LogLevel.Debug);

// Register Praetorium.Bridge core services
builder.Services.AddPraetoriumBridge();
builder.Services.AddCopilotProvider();

// Register web services
builder.Services.AddSingleton<ConfigurationService>();
builder.Services.AddSingleton<DashboardBridgeHooks>();

// Override the default IBridgeHooks with DashboardBridgeHooks
builder.Services.AddSingleton<IBridgeHooks>(sp => sp.GetRequiredService<DashboardBridgeHooks>());

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

app.Run();
