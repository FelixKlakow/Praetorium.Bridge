using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Praetorium.Bridge.Sessions;

/// <summary>
/// In-memory implementation of the session store using concurrent collections.
/// </summary>
public class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();

    /// <summary>
    /// Gets a session by its unique identifier.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The session information, or null if not found.</returns>
    public Task<SessionInfo?> GetAsync(string sessionId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        ct.ThrowIfCancellationRequested();

        var found = _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(found ? session : null);
    }

    /// <summary>
    /// Gets a session by its tool name and reference ID.
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <param name="referenceId">The reference ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The session information, or null if not found.</returns>
    public Task<SessionInfo?> GetByReferenceAsync(string toolName, string referenceId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(toolName))
            throw new ArgumentException("Tool name cannot be null or empty.", nameof(toolName));

        if (string.IsNullOrEmpty(referenceId))
            throw new ArgumentException("Reference ID cannot be null or empty.", nameof(referenceId));

        ct.ThrowIfCancellationRequested();

        var session = _sessions.Values.FirstOrDefault(s =>
            s.ToolName == toolName && s.ReferenceId == referenceId);

        return Task.FromResult<SessionInfo?>(session);
    }

    /// <summary>
    /// Gets a session by its tool name and connection ID.
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <param name="connectionId">The connection ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The session information, or null if not found.</returns>
    public Task<SessionInfo?> GetByConnectionAsync(string toolName, string connectionId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(toolName))
            throw new ArgumentException("Tool name cannot be null or empty.", nameof(toolName));

        if (string.IsNullOrEmpty(connectionId))
            throw new ArgumentException("Connection ID cannot be null or empty.", nameof(connectionId));

        ct.ThrowIfCancellationRequested();

        var session = _sessions.Values.FirstOrDefault(s =>
            s.ToolName == toolName && s.ConnectionId == connectionId);

        return Task.FromResult<SessionInfo?>(session);
    }

    /// <summary>
    /// Gets the global session for a tool (when using Global session mode).
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The session information, or null if not found.</returns>
    public Task<SessionInfo?> GetGlobalAsync(string toolName, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(toolName))
            throw new ArgumentException("Tool name cannot be null or empty.", nameof(toolName));

        ct.ThrowIfCancellationRequested();

        var session = _sessions.Values.FirstOrDefault(s =>
            s.ToolName == toolName && s.ReferenceId == null && s.ConnectionId == null);

        return Task.FromResult<SessionInfo?>(session);
    }

    /// <summary>
    /// Gets all sessions.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of all sessions.</returns>
    public Task<IReadOnlyList<SessionInfo>> GetAllAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var sessions = _sessions.Values.ToList().AsReadOnly();
        return Task.FromResult<IReadOnlyList<SessionInfo>>(sessions);
    }

    /// <summary>
    /// Gets all sessions in a particular state.
    /// </summary>
    /// <param name="state">The session state to filter by.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only list of sessions matching the state.</returns>
    public Task<IReadOnlyList<SessionInfo>> GetByStateAsync(SessionState state, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var sessions = _sessions.Values
            .Where(s => s.State == state)
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<SessionInfo>>(sessions);
    }

    /// <summary>
    /// Stores or updates a session.
    /// </summary>
    /// <param name="session">The session information to store.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task SetAsync(SessionInfo session, CancellationToken ct)
    {
        if (session == null)
            throw new ArgumentNullException(nameof(session));

        ct.ThrowIfCancellationRequested();

        _sessions[session.SessionId] = session;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Removes a session.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session to remove.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task RemoveAsync(string sessionId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        ct.ThrowIfCancellationRequested();

        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }
}
