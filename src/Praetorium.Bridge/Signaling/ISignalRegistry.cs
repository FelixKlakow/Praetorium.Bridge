using System;
using System.Threading;
using System.Threading.Tasks;

namespace Praetorium.Bridge.Signaling;



/// <summary>
/// Manages signal delivery between the internal agent and the external caller
/// for a session. Two independent FIFO channels are maintained per session:
/// <list type="bullet">
///   <item>
///     <description><b>Outbound</b> — produced by the agent via signaling tools,
///     consumed by the external caller's poll. Multiple outbound signals are
///     queued so nothing is lost when the agent produces faster than the caller
///     polls.</description>
///   </item>
///   <item>
///     <description><b>Inbound</b> — produced by the external caller (typically
///     a reply to a blocking <c>request_input</c> tool) and consumed by the
///     blocking signaling tool's waiter.</description>
///   </item>
/// </list>
/// Keeping the two channels separate prevents blocking tools from
/// accidentally re-consuming their own outgoing payload.
/// </summary>
public interface ISignalRegistry
{
    /// <summary>
    /// Posts an outbound signal (agent → external caller) to the session. If
    /// no caller is currently waiting the signal is queued FIFO and returned
    /// on the next <see cref="WaitOutboundAsync"/> call.
    /// </summary>
    void SignalOutbound(string sessionId, SignalResult result);

    /// <summary>
    /// Waits for the next outbound signal (external caller polling for agent
    /// output). Returns immediately if one is already queued.
    /// </summary>
    Task<SignalResult> WaitOutboundAsync(string sessionId, TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// Posts an inbound signal (external caller → agent) to the session —
    /// typically the reply to a blocking signaling tool. Queued FIFO if no
    /// blocking waiter is registered yet.
    /// </summary>
    void SignalInbound(string sessionId, SignalResult result);

    /// <summary>
    /// Waits for the next inbound signal (blocking signaling tool waiting on
    /// the caller's reply). Returns immediately if one is already queued.
    /// </summary>
    Task<SignalResult> WaitInboundAsync(string sessionId, TimeSpan timeout, CancellationToken ct);

    /// <summary>
    /// Signals disconnect to every session bound to the given connection.
    /// The disconnect is delivered on <b>both</b> channels so any pending
    /// external poll and any pending blocking tool both unblock.
    /// </summary>
    void SignalDisconnect(string connectionId);

    /// <summary>
    /// Associates a session with a connection for disconnect propagation.
    /// </summary>
    void RegisterConnectionBinding(string sessionId, string connectionId);

    /// <summary>
    /// Removes all state for a session: clears both channel queues, cancels
    /// any pending waiter on either channel, drops the connection binding.
    /// </summary>
    void RemoveSession(string sessionId);

    /// <summary>
    /// Returns <c>true</c> when a blocking signaling tool is currently parked on
    /// the session's inbound channel waiting for a caller reply. Used by the
    /// dispatcher to distinguish a <c>Resume</c> dispatch (agent blocked, payload
    /// expected) from a <c>Rejoin</c> dispatch (agent running, pure poll).
    /// </summary>
    bool HasPendingInboundWaiter(string sessionId);

    /// <summary>
    /// Returns <c>true</c> when at least one outbound signal is queued for the
    /// session and no caller is currently waiting to consume it. Used by the
    /// dispatcher to detect that more outbound signals are still pending after
    /// dequeuing one — so a payload marked <c>complete</c> by the agent is
    /// demoted to <c>partial</c> and the caller is told to keep draining.
    /// </summary>
    bool HasPendingOutbound(string sessionId);

    /// <summary>
    /// Delivers a <see cref="SignalResult.Reset"/> on both channels for the
    /// given session so any in-flight waiter (an agent blocked inside a
    /// signaling tool, or a dispatcher blocked on the outbound channel) is
    /// released. Used by the dashboard to manually unstick a session.
    /// </summary>
    void CancelWaiters(string sessionId);

    /// <summary>
    /// Raised whenever an outbound or inbound signal is posted to any session.
    /// Consumers (e.g. the dashboard) can observe every signal passing through
    /// the registry without participating in the signaling protocol itself.
    /// </summary>
    event Action<SignalingEvent>? Signaled;
}
