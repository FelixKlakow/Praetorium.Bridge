using GitHub.Copilot.SDK;
using Microsoft.Extensions.DependencyInjection;
using Praetorium.Bridge.Agents;
using Praetorium.Bridge.CopilotProvider.InternalMcp;

namespace Praetorium.Bridge.CopilotProvider;

/// <summary>
/// Extension methods for registering the Copilot agent provider in a dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the GitHub Copilot agent provider with the specified options.
    /// Authentication is handled automatically via local GitHub credentials (gh CLI, VS, or VS Code).
    /// </summary>
    /// <param name="services">The service collection to register the provider in.</param>
    /// <param name="configureOptions">Action to configure the provider options.</param>
    /// <returns>The service collection for fluent configuration.</returns>
    public static IServiceCollection AddCopilotProvider(
        this IServiceCollection services,
        Action<CopilotProviderOptions>? configureOptions = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        var options = new CopilotProviderOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);

        // Register a singleton CopilotClient. AutoStart = true (SDK default) so the
        // CLI process is launched automatically on first use — no blocking call needed here.
        services.AddSingleton(sp =>
        {
            var resolvedOptions = sp.GetRequiredService<CopilotProviderOptions>();
            var cliPath = CopilotCliLocator.Locate(resolvedOptions.CliPath);
            var clientOptions = cliPath != null
                ? new CopilotClientOptions { CliPath = cliPath }
                : new CopilotClientOptions();
            return new CopilotClient(clientOptions);
        });

        services.AddSingleton<IAgentProvider, CopilotAgentProvider>();
        services.AddSingleton<IInternalMcpRegistry, InternalMcpRegistry>();
        return services;
    }
}
