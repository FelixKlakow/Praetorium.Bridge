using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Praetorium.Bridge.Signaling;

/// <summary>
/// Thread-safe implementation of <see cref="ISignalRegistry"/>. Each session
/// owns two independent FIFO <see cref="Channel"/> instances: one for outbound
/// (agent → caller) and one for inbound (caller → agent) signals. Signals
/// posted while no waiter is registered are queued so nothing is lost.
/// </summary>
public class SignalRegistry : ISignalRegistry
{
    private sealed class Channel
    {
        public readonly object Lock = new();
        public readonly Queue<SignalResult> Pending = new();
        public TaskCompletionSource<SignalResult>? Waiter;
    }

    private sealed class SessionSlot
    {
        public readonly Channel Outbound = new();
        public readonly Channel Inbound = new();
    }

    private readonly ConcurrentDictionary<string, SessionSlot> _sessions = new();
    private readonly ConcurrentDictionary<string, string> _sessionConnectionBindings = new();

    /// <inheritdoc />
    public event Action<SignalingEvent>? Signaled;

    private SessionSlot GetOrCreateSlot(string sessionId) =>
        _sessions.GetOrAdd(sessionId, _ => new SessionSlot());

    /// <inheritdoc />
    public void SignalOutbound(string sessionId, SignalResult result)
    {
        Post(GetOrCreateSlot(EnsureId(sessionId)).Outbound, result);
        RaiseSignaled(sessionId, SignalingDirection.Outbound, result);
    }

    /// <inheritdoc />
    public Task<SignalResult> WaitOutboundAsync(string sessionId, TimeSpan timeout, CancellationToken ct) =>
        WaitAsync(GetOrCreateSlot(EnsureId(sessionId)).Outbound, sessionId, timeout, ct);

    /// <inheritdoc />
    public void SignalInbound(string sessionId, SignalResult result)
    {
        Post(GetOrCreateSlot(EnsureId(sessionId)).Inbound, result);
        RaiseSignaled(sessionId, SignalingDirection.Inbound, result);
    }

    /// <inheritdoc />
    public Task<SignalResult> WaitInboundAsync(string sessionId, TimeSpan timeout, CancellationToken ct) =>
        WaitAsync(GetOrCreateSlot(EnsureId(sessionId)).Inbound, sessionId, timeout, ct);

    /// <inheritdoc />
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
            // Deliver on both channels so whichever side is waiting unblocks.
            var slot = GetOrCreateSlot(sessionId);
            var disconnect = SignalResult.Disconnect();
            Post(slot.Outbound, disconnect);
            RaiseSignaled(sessionId, SignalingDirection.Outbound, disconnect);
            Post(slot.Inbound, disconnect);
            RaiseSignaled(sessionId, SignalingDirection.Inbound, disconnect);
        }
    }

    /// <inheritdoc />
    public void CancelWaiters(string sessionId)
    {
        EnsureId(sessionId);

        var slot = GetOrCreateSlot(sessionId);
        var reset = SignalResult.Reset();
        Post(slot.Outbound, reset);
        RaiseSignaled(sessionId, SignalingDirection.Outbound, reset);
        Post(slot.Inbound, reset);
        RaiseSignaled(sessionId, SignalingDirection.Inbound, reset);
    }

    private void RaiseSignaled(string sessionId, SignalingDirection direction, SignalResult result)
    {
        var handler = Signaled;
        if (handler == null) return;
        var evt = new SignalingEvent(DateTimeOffset.UtcNow, sessionId, direction, result.Type, result.Data);
        try { handler(evt); }
        catch { /* observers must not break the signaling protocol */ }
    }

    /// <inheritdoc />
    public void RegisterConnectionBinding(string sessionId, string connectionId)
    {
        EnsureId(sessionId);
        if (string.IsNullOrEmpty(connectionId))
            throw new ArgumentException("Connection ID cannot be null or empty.", nameof(connectionId));

        _sessionConnectionBindings[sessionId] = connectionId;
    }

    /// <inheritdoc />
    public void RemoveSession(string sessionId)
    {
        EnsureId(sessionId);

        if (_sessions.TryRemove(sessionId, out var slot))
        {
            Drain(slot.Outbound);
            Drain(slot.Inbound);
        }

        _sessionConnectionBindings.TryRemove(sessionId, out _);
    }

    private static string EnsureId(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
            throw new ArgumentException("Session ID cannot be null or empty.", nameof(sessionId));
        return sessionId;
    }

    private static void Post(Channel channel, SignalResult result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        TaskCompletionSource<SignalResult>? waiter = null;
        lock (channel.Lock)
        {
            if (channel.Waiter != null)
            {
                waiter = channel.Waiter;
                channel.Waiter = null;
            }
            else
            {
                channel.Pending.Enqueue(result);
            }
        }

        waiter?.TrySetResult(result);
    }

    private static async Task<SignalResult> WaitAsync(
        Channel channel,
        string sessionId,
        TimeSpan timeout,
        CancellationToken ct)
    {
        TaskCompletionSource<SignalResult> tcs;
        lock (channel.Lock)
        {
            if (channel.Pending.Count > 0)
            {
                return channel.Pending.Dequeue();
            }

            if (channel.Waiter != null)
            {
                throw new InvalidOperationException(
                    $"Session {sessionId} already has a pending waiter on this channel.");
            }

            tcs = new TaskCompletionSource<SignalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            channel.Waiter = tcs;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var delayTask = Task.Delay(Timeout.Infinite, cts.Token);
            var completed = await Task.WhenAny(tcs.Task, delayTask).ConfigureAwait(false);

            if (completed == tcs.Task)
            {
                return await tcs.Task.ConfigureAwait(false);
            }

            return SignalResult.Timeout();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return SignalResult.Timeout();
        }
        finally
        {
            lock (channel.Lock)
            {
                if (ReferenceEquals(channel.Waiter, tcs))
                {
                    channel.Waiter = null;
                }
            }

            if (!tcs.Task.IsCompleted)
            {
                tcs.TrySetCanceled();
            }
        }
    }

    private static void Drain(Channel channel)
    {
        TaskCompletionSource<SignalResult>? waiter;
        lock (channel.Lock)
        {
            waiter = channel.Waiter;
            channel.Waiter = null;
            channel.Pending.Clear();
        }

        waiter?.TrySetCanceled();
    }
}
