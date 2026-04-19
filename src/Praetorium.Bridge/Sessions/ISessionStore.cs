using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Praetorium.Bridge.Sessions;

/// <summary>
/// Interface for persistent storage of session information.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Gets a session by its unique identifier.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The session information, or null if not found.</returns>
    Task<SessionInfo?> GetAsync(string sessionId, CancellationToken ct);

    /// <summary>
    /// Gets a session by its tool name and reference ID.
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <param name="referenceId">The reference ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The session information, or null if not found.</returns>
    Task<SessionInfo?> GetByReferenceAsync(string toolName, string referenceId, CancellationToken ct);

    /// <summary>
    /// Gets a session by its tool name and connection ID.
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The session information, or null if not found.</returns>
    Task<SessionInfo?> GetByConnectionAsync(string toolName, string connectionId, CancellationToken ct);

    /// <summary>
    /// Gets the global session for a tool (when using Global session mode).
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The session information, or null if not found.</returns>
    Task<SessionInfo?> GetGlobalAsync(string toolName, CancellationToken ct);

    /// <summary>
    /// Gets all sessions.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of all sessions.</returns>
    Task<IReadOnlyList<SessionInfo>> GetAllAsync(CancellationToken ct);

    /// <summary>
    /// Gets all sessions in a particular state.
    /// </summary>
    /// <param name="state">The session state to filter by.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of sessions matching the state.</returns>
    Task<IReadOnlyList<SessionInfo>> GetByStateAsync(SessionState state, CancellationToken ct);

    /// <summary>
    /// Stores or updates a session.
    /// </summary>
    /// <param name="session">The session information to store.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task SetAsync(SessionInfo session, CancellationToken ct);

    /// <summary>
    /// Removes a session.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RemoveAsync(string sessionId, CancellationToken ct);
}
