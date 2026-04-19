using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Praetorium.Bridge.Signaling;

/// <summary>
/// Thread-safe implementation of the signal registry using concurrent collections.
/// </summary>
public class SignalRegistry : ISignalRegistry
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<SignalResult>> _pendingWaiters = new();
    private readonly ConcurrentDictionary<string, string> _sessionConnectionBindings = new();

    /// <summary>
    /// Waits for a signal to be delivered to the specified session, with a timeout.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session.</param>
    /// <param name="timeout">The maximum time to wait for a signal.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes with the delivered signal, or a timeout signal if the timeout expires.</returns>
    public async Task<SignalResult> WaitForSignalAsync(string sessionId, TimeSpan timeout, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        var tcs = new TaskCompletionSource<SignalResult>();
        if (!_pendingWaiters.TryAdd(sessionId, tcs))
        {
            throw new InvalidOperationException($"Session {sessionId} already has a pending waiter.");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var delayTask = Task.Delay(Timeout.Infinite, cts.Token);
            var completedTask = await Task.WhenAny(tcs.Task, delayTask).ConfigureAwait(false);

            if (completedTask == tcs.Task)
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            else
            {
                return SignalResult.Timeout();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return SignalResult.Timeout();
        }
        finally
        {
            _pendingWaiters.TryRemove(sessionId, out _);
            if (!tcs.Task.IsCompleted)
            {
                tcs.TrySetCanceled();
            }
        }
    }

    /// <summary>
    /// Delivers a signal to a session, unblocking any waiter.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session.</param>
    /// <param name="result">The signal result to deliver.</param>
    public void Signal(string sessionId, SignalResult result)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        if (result == null)
            throw new ArgumentNullException(nameof(result));

        if (_pendingWaiters.TryRemove(sessionId, out var tcs))
        {
            tcs.TrySetResult(result);
        }
    }

    /// <summary>
    /// Signals a disconnect to all sessions associated with a connection.
    /// </summary>
    /// <param name="connectionId">The connection identifier.</param>
    public void SignalDisconnect(string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
            throw new ArgumentException("Connection ID cannot be null or empty.", nameof(connectionId));

        var sessionsToSignal = _sessionConnectionBindings
            .Where(kvp => kvp.Value == connectionId)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var sessionId in sessionsToSignal)
        {
            Signal(sessionId, SignalResult.Disconnect());
        }
    }

    /// <summary>
    /// Associates a session with a connection for disconnect signaling.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session.</param>
    /// <param name="connectionId">The connection identifier.</param>
    public void RegisterConnectionBinding(string sessionId, string connectionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        if (string.IsNullOrEmpty(connectionId))
            throw new ArgumentException("Connection ID cannot be null or empty.", nameof(connectionId));

        _sessionConnectionBindings[sessionId] = connectionId;
    }

    /// <summary>
    /// Removes a session and cleans up any pending waiters and bindings.
    /// </summary>
    /// <param name="sessionId">The unique identifier of the session.</param>
    public void RemoveSession(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));

        if (_pendingWaiters.TryRemove(sessionId, out var tcs))
        {
            if (!tcs.Task.IsCompleted)
            {
                tcs.TrySetCanceled();
            }
        }

        _sessionConnectionBindings.TryRemove(sessionId, out _);
    }
}
