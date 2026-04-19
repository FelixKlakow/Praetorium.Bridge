using System.Threading;
using System.Threading.Tasks;

namespace Praetorium.Bridge.Agents;

/// <summary>
/// Represents an active agent session that can process messages.
/// Provider-agnostic abstraction over the underlying SDK session.
/// </summary>
public interface IAgentSession
{
    /// <summary>
    /// Sends a message to the agent and waits for the response.
    /// </summary>
    /// <param name="message">The message/prompt to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The agent's response text.</returns>
    Task<string> SendAsync(string message, CancellationToken ct);
}
