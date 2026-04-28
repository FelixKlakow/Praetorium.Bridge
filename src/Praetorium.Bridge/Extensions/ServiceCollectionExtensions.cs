using System;
using Microsoft.Extensions.DependencyInjection;
using Praetorium.Bridge.Agents;
using Praetorium.Bridge.Configuration;
using Praetorium.Bridge.Hooks;
using Praetorium.Bridge.Mcp;
using Praetorium.Bridge.Prompts;
using Praetorium.Bridge.Sessions;
using Praetorium.Bridge.Signaling;
using Praetorium.Bridge.Tools;

namespace Praetorium.Bridge.Extensions;

/// <summary>
/// Extension methods for registering Praetorium.Bridge services in a dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all core Praetorium.Bridge services in the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to register services in.</param>
    /// <param name="configFilePath">The path to the bridge configuration JSON file. Defaults to <see cref="BridgePaths.DefaultConfigFilePath"/>.</param>
    /// <param name="configure">Optional callback to configure bridge options such as hooks.</param>
    /// <returns>The service collection for fluent configuration.</returns>
    public static IServiceCollection AddPraetoriumBridge(
        this IServiceCollection services,
        string? configFilePath = null,
        Action<BridgeOptions>? configure = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        // Create and configure bridge options
        var options = new BridgeOptions();
        if (!string.IsNullOrEmpty(configFilePath))
            options.ConfigFilePath = configFilePath;
        configure?.Invoke(options);

        if (string.IsNullOrEmpty(options.ConfigFilePath))
            throw new ArgumentException("Config file path cannot be null or empty.");

        // Ensure the AppData directories exist before accessing configuration
        BridgePaths.EnsureDirectoriesExist();

        // Register configuration provider as singleton
        services.AddSingleton<IConfigurationProvider>(sp =>
            new JsonConfigurationProvider(options.ConfigFilePath));

        // Register signal registry as singleton
        services.AddSingleton<ISignalRegistry, SignalRegistry>();

        // Register session store as singleton
        services.AddSingleton<ISessionStore, InMemorySessionStore>();

        // Register session manager as singleton and hosted service
        services.AddSingleton<SessionManager>();
        services.AddSingleton<ISessionManager>(sp => sp.GetRequiredService<SessionManager>());
        services.AddHostedService(sp => sp.GetRequiredService<SessionManager>());

        // Register tool parameter binder as transient
        services.AddTransient<ToolParameterBinder>();

        // Register prompt resolver as singleton
        var configDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(options.ConfigFilePath)) ?? ".";
        services.AddSingleton<IPromptResolver>(sp =>
            new PromptResolver(configDir));

        // Register tool dispatcher as singleton
        services.AddSingleton<IToolDispatcher, ToolDispatcher>();

        // Register MCP server builder as singleton
        services.AddSingleton<McpServerTracker>();
        services.AddSingleton<McpServerBuilder>();

        // Register dynamic tool handler as singleton
        services.AddSingleton<DynamicToolHandler>();

        // Register bridge hooks with optional custom configuration
        if (options.ConfigureHooks != null)
        {
            services.AddSingleton<IBridgeHooks>(sp =>
            {
                var hooks = new NullBridgeHooks();
                options.ConfigureHooks(hooks);
                return hooks;
            });
        }
        else
        {
            services.AddSingleton<IBridgeHooks, NullBridgeHooks>();
        }

        return services;
    }

    /// <summary>
    /// Registers a custom agent provider implementation in the dependency injection container.
    /// </summary>
    /// <typeparam name="T">The type of the custom agent provider. Must implement IAgentProvider.</typeparam>
    /// <param name="services">The service collection to register the provider in.</param>
    /// <returns>The service collection for fluent configuration.</returns>
    public static IServiceCollection AddPraetoriumBridgeProvider<T>(this IServiceCollection services)
        where T : class, IAgentProvider
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        services.AddSingleton<IAgentProvider, T>();
        return services;
    }
}

/// <summary>
/// Configuration options for Praetorium.Bridge.
/// </summary>
public class BridgeOptions
{
    /// <summary>
    /// Gets or sets the path to the bridge configuration JSON file.
    /// Defaults to <see cref="Configuration.BridgePaths.DefaultConfigFilePath"/>.
    /// </summary>
    public string ConfigFilePath { get; set; } = Configuration.BridgePaths.DefaultConfigFilePath;

    /// <summary>
    /// Gets or sets the optional callback to configure bridge hooks.
    /// </summary>
    public Action<IBridgeHooks>? ConfigureHooks { get; set; }
}
