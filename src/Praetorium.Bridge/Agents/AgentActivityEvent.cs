using System;

namespace Praetorium.Bridge.Agents;

/// <summary>
/// The kind of activity raised by an <see cref="IAgentSessionObservable"/>
/// implementation. Used by the dashboard to colour-code the live transcript.
/// </summary>
public enum AgentActivityKind
{
    /// <summary>
    /// A chunk of assistant-visible text produced by the agent.
    /// </summary>
    AssistantMessage,

    /// <summary>
    /// The agent started executing a tool (signaling or agent-side).
    /// </summary>
    ToolStart,

    /// <summary>
    /// The agent finished executing a tool.
    /// </summary>
    ToolComplete,

    /// <summary>
    /// The underlying session reported an error.
    /// </summary>
    Error,

    /// <summary>
    /// The underlying session went idle (end of a turn).
    /// </summary>
    Idle,
}

/// <summary>
/// A single observation of an agent session's internal activity as surfaced by
/// the underlying agent SDK. Shape is provider-neutral so the dashboard can
/// render a uniform transcript regardless of which <see cref="IAgentProvider"/>
/// produced the session.
/// </summary>
/// <param name="Timestamp">When the event was produced.</param>
/// <param name="Kind">What kind of event this is.</param>
/// <param name="Content">Text content associated with the event (assistant text, error message, etc.), if any.</param>
/// <param name="ToolName">Name of the tool being invoked, for <see cref="AgentActivityKind.ToolStart"/> and <see cref="AgentActivityKind.ToolComplete"/>.</param>
/// <param name="ArgumentsJson">Serialized tool arguments, if the SDK surfaced them.</param>
/// <param name="Success">Tool execution success flag, for <see cref="AgentActivityKind.ToolComplete"/>.</param>
public sealed record AgentActivityEvent(
    DateTimeOffset Timestamp,
    AgentActivityKind Kind,
    string? Content = null,
    string? ToolName = null,
    string? ArgumentsJson = null,
    bool? Success = null);
