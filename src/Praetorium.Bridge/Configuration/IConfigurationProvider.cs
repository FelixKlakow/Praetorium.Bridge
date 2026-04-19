using System;
using System.Threading;
using System.Threading.Tasks;

namespace Praetorium.Bridge.Configuration;

/// <summary>
/// Interface for providing and managing bridge configuration.
/// </summary>
public interface IConfigurationProvider
{
    /// <summary>
    /// Gets the current bridge configuration.
    /// </summary>
    BridgeConfiguration Configuration { get; }

    /// <summary>
    /// Gets the absolute directory that contains the configuration file. Relative paths in the configuration
    /// (such as prompt files) are resolved against this directory.
    /// </summary>
    string ConfigDirectory { get; }

    /// <summary>
    /// Raised when the configuration is reloaded.
    /// </summary>
    event Action<BridgeConfiguration>? OnConfigurationChanged;

    /// <summary>
    /// Reloads the configuration asynchronously.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ReloadAsync(CancellationToken ct);

    /// <summary>
    /// Saves the configuration asynchronously.
    /// </summary>
    /// <param name="config">The configuration to save.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SaveAsync(BridgeConfiguration config, CancellationToken ct);
}
