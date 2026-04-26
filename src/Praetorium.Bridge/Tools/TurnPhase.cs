namespace Praetorium.Bridge.Tools;

/// <summary>
/// Dispatcher-visible lifecycle phase for a single tool invocation, determined
/// from the session's running-turn state. Drives parameter binding rules and
/// the dispatcher's behaviour for the incoming call.
/// </summary>
public enum TurnPhase
{
    /// <summary>
    /// No running turn (new session, or previous turn ended). The dispatcher will
    /// render the tool prompt and call <see cref="Agents.IAgentSession.SendAsync"/>.
    /// Binder enforces required <see cref="Configuration.ParameterKind.Prompt"/>
    /// parameters.
    /// </summary>
    NewTurn = 0,

    /// <summary>
    /// A turn is running and the agent is blocked inside a blocking signaling
    /// tool (previous dispatch returned <c>input_requested</c>). The dispatcher
    /// will post the caller's reply on the inbound channel. Binder enforces
    /// required <see cref="Configuration.ParameterKind.Resume"/> parameters.
    /// </summary>
    Resume = 1,

    /// <summary>
    /// A turn is running and the agent is NOT blocked — the previous dispatch
    /// returned a non-blocking <c>partial</c> payload. The dispatcher only
    /// subscribes for the next outbound signal; no payload is required from
    /// the caller. Binder enforces nothing.
    /// </summary>
    Rejoin = 2,
}
