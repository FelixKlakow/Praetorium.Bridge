using System;

namespace Praetorium.Bridge.Agents;

/// <summary>
/// Optional companion interface to <see cref="IAgentSession"/>: implementations
/// that can surface internal SDK events (assistant text, tool calls, errors)
/// expose them via <see cref="ActivityRaised"/> so the dashboard can render a
/// live transcript of what the agent is doing.
/// <para>
/// The interface is deliberately separate from <see cref="IAgentSession"/> so
/// providers and test doubles that do not care about event observation do not
/// need to implement it. Consumers should check at runtime with a pattern match.
/// </para>
/// </summary>
public interface IAgentSessionObservable
{
    /// <summary>
    /// Raised when the underlying session produces an observable event. Handlers
    /// should be non-blocking — the event is raised on whatever thread the SDK
    /// delivers events on.
    /// </summary>
    event Action<AgentActivityEvent>? ActivityRaised;
}
