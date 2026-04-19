using System.Threading;
using System.Threading.Tasks;

namespace Praetorium.Bridge.Agents;

/// <summary>
/// Interface for agent provider implementations that create and manage AI agents.
/// </summary>
public interface IAgentProvider
{
    /// <summary>
    /// Gets the name of this agent provider.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Creates a new agent session for the given context.
    /// </summary>
    /// <param name="context">The context for creating the agent.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation that returns the agent session.</returns>
    Task<IAgentSession> CreateAgentAsync(AgentContext context, CancellationToken ct);

    /// <summary>
    /// Determines whether this provider supports a specific model.
    /// </summary>
    /// <param name="model">The model name to check.</param>
    /// <returns>True if the model is supported; otherwise, false.</returns>
    bool SupportsModel(string model);

    /// <summary>
    /// Determines whether this provider supports reasoning effort configuration for a specific model.
    /// Model capability detection is queried from the provider/SDK at runtime, not hardcoded.
    /// </summary>
    /// <param name="model">The model name to check.</param>
    /// <returns>True if reasoning effort is supported; otherwise, false.</returns>
    bool SupportsReasoningEffort(string model);

    /// <summary>
    /// Gets the capabilities of a specific model at runtime.
    /// This queries the underlying SDK/service to determine actual capabilities.
    /// </summary>
    /// <param name="model">The model name to query capabilities for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The capabilities of the model, or null if the model is not supported.</returns>
    Task<AgentCapabilities?> GetModelCapabilitiesAsync(string model, CancellationToken ct);

    /// <summary>
    /// Gets all available model deployment names from the provider at runtime.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of model/deployment names available to use.</returns>
    Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct);
}
