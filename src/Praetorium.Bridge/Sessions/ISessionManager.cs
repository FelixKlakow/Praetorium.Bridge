using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Praetorium.Bridge.Agents;
using Praetorium.Bridge.Configuration;
using Praetorium.Bridge.Signaling;

namespace Praetorium.Bridge.Sessions;

/// <summary>
/// Interface for managing session lifecycle and pooling.
/// </summary>
public interface ISessionManager
{
    /// <summary>
    /// Gets an existing session or creates a new one based on the session mode and configuration.
    /// </summary>
    /// <param name="toolName">The name of the tool for which the session is being requested.</param>
    /// <param name="referenceId">Optional reference ID for PerReference session mode.</param>
    /// <param name="connectionId">Optional connection ID for PerConnection session mode.</param>
    /// <param name="mode">The session mode (PerConnection, PerReference, or Global).</param>
    /// <param name="agentConfig">The agent configuration to use for spawning.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tuple containing the session info and a flag indicating if it was newly created.</returns>
    Task<(SessionInfo Session, bool IsNew)> GetOrCreateSessionAsync(
        string toolName,
        string? referenceId,
        string? connectionId,
        SessionMode mode,
        AgentConfiguration agentConfig,
        CancellationToken ct);

    /// <summary>
    /// Resets a session by stopping the current agent and moving it to pooled state.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session to reset.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ResetSessionAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Moves a session to the pooled state for reuse.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session to pool.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PoolSessionAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Updates the state of a session.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session.</param>
    /// <param name="state">The new state for the session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpdateStateAsync(string sessionId, SessionState state, CancellationToken ct);

    /// <summary>
    /// Marks a session as crashed due to an unhandled exception.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session.</param>
    /// <param name="ex">The exception that caused the crash.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task MarkCrashedAsync(string sessionId, Exception ex, CancellationToken ct);

    /// <summary>
    /// Removes a session completely from the store.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RemoveSessionAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Gets all active sessions (those not in Pooled or Crashed state).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of active sessions.</returns>
    Task<IReadOnlyList<SessionInfo>> GetActiveSessionsAsync(CancellationToken ct);

    /// <summary>
    /// Gets all sessions.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of all sessions.</returns>
    Task<IReadOnlyList<SessionInfo>> GetAllSessionsAsync(CancellationToken ct);

    /// <summary>
    /// Notifies the session manager of a caller disconnect event.
    /// </summary>
    /// <param name="connectionId">The connection identifier that disconnected.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task NotifyDisconnectAsync(string connectionId, CancellationToken ct);

    /// <summary>
    /// Gets the active AI agent for a given session ID.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session.</param>
    /// <returns>The agent session if active; otherwise null.</returns>
    IAgentSession? GetActiveAgent(string sessionId);

    /// <summary>
    /// Creates an agent session for an existing session.
    /// The provider handles session management and tool calling internally.
    /// </summary>
    /// <param name="sessionId">The session ID to create the agent for.</param>
    /// <param name="toolName">The tool name the agent is handling.</param>
    /// <param name="prompt">The system prompt for the agent.</param>
    /// <param name="agentConfig">The agent configuration.</param>
    /// <param name="toolSources">The MCP tool sources for the agent.</param>
    /// <param name="signalingTools">The signaling tool definitions.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created agent session.</returns>
    Task<IAgentSession> CreateAgentAsync(
        string sessionId,
        string toolName,
        string prompt,
        AgentConfiguration agentConfig,
        List<AgentToolSource> toolSources,
        List<SignalingToolDefinition> signalingTools,
        CancellationToken ct);

    /// <summary>
    /// Registers the <see cref="IAgentSession.SendAsync"/> task representing the currently
    /// running agent turn for a session. The dispatcher uses this to race outbound signals
    /// against turn completion so it never blocks on an agent that is itself parked inside
    /// a blocking signaling tool.
    /// </summary>
    /// <param name="sessionId">The session the turn belongs to.</param>
    /// <param name="turnTask">The task returned by <see cref="IAgentSession.SendAsync"/>.</param>
    void SetRunningTurn(string sessionId, Task<string> turnTask);

    /// <summary>
    /// Gets the currently running agent turn task for a session, or <c>null</c> when no
    /// turn is tracked. The task may already be completed — callers must check
    /// <see cref="Task.IsCompleted"/> before reusing it as an in-flight turn.
    /// </summary>
    Task<string>? GetRunningTurn(string sessionId);

    /// <summary>
    /// Clears the running turn tracking for a session. Called when the turn has been
    /// fully consumed or the session is torn down.
    /// </summary>
    void ClearRunningTurn(string sessionId);

    /// <summary>
    /// Releases any in-flight signaling waiters for the session by delivering a
    /// <see cref="SignalType.Reset"/> on both channels. Used by the dashboard to
    /// manually unstick an agent that is parked inside a blocking signaling tool
    /// or a dispatcher parked on the outbound channel. Returns <c>true</c> if the
    /// session exists and cancellation was issued.
    /// </summary>
    Task<bool> CancelSessionWaitsAsync(string sessionId, CancellationToken ct);
}
