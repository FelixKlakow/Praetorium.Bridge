namespace Praetorium.Bridge.Signaling;

/// <summary>
/// Enumeration of signal types that can be delivered to sessions.
/// </summary>
public enum SignalType
{
    /// <summary>
    /// Signal containing input data from the caller.
    /// </summary>
    Input,

    /// <summary>
    /// Signal indicating that the caller has disconnected.
    /// </summary>
    Disconnect,

    /// <summary>
    /// Signal indicating that the session should be reset.
    /// </summary>
    Reset,

    /// <summary>
    /// Signal indicating that a timeout has occurred.
    /// </summary>
    Timeout
}
