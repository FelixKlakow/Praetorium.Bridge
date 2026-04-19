using System;
using System.Threading;
using System.Threading.Tasks;

namespace Praetorium.Bridge.Signaling;

/// <summary>
/// Interface for managing signals delivered to sessions.
/// </summary>
public interface ISignalRegistry
{
    /// <summary>
    /// Waits for a signal to be delivered to the specified session, with a timeout.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session.</param>
    /// <param name="timeout">The maximum time to wait for a signal.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes with the delivered signal, or a timeout signal if the timeout expires.</returns>
    Task<SignalResult> WaitForSignalAsync(string sessionId, TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// Delivers a signal to a session, unblocking any waiter.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session.</param>
    /// <param name="result">The signal result to deliver.</param>
    void Signal(string sessionId, SignalResult result);

    /// <summary>
    /// Signals a disconnect to all sessions associated with a connection.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    void SignalDisconnect(string connectionId);

    /// <summary>
    /// Associates a session with a connection for disconnect signaling.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session.</param>
    /// <param name="connectionId">The connection identifier.</param>
    void RegisterConnectionBinding(string sessionId, string connectionId);

    /// <summary>
    /// Removes a session and cleans up any pending waiters and bindings.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session.</param>
    void RemoveSession(string sessionId);
}
