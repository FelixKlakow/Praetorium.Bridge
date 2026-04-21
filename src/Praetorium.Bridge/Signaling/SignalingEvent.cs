using System;

namespace Praetorium.Bridge.Signaling;

/// <summary>
/// Direction of a signal observed on the registry. Matches the two channels
/// exposed by <see cref="ISignalRegistry"/>.
/// </summary>
public enum SignalingDirection
{
    /// <summary>
    /// Agent → external caller (produced by a signaling tool, drained by
    /// the dispatcher waiting on the outbound channel).
    /// </summary>
    Outbound,

    /// <summary>
    /// External caller → agent (produced by the dispatcher when a continuation
    /// reply arrives, drained by a blocking signaling tool).
    /// </summary>
    Inbound,
}

/// <summary>
/// A single signal post observed on the registry. Consumed by the dashboard to
/// render the live signaling timeline for each session.
/// </summary>
public sealed record SignalingEvent(
    DateTimeOffset Timestamp,
    string SessionId,
    SignalingDirection Direction,
    SignalType Type,
    object? Data);
