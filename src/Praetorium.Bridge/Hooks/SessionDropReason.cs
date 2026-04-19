namespace Praetorium.Bridge.Hooks;

/// <summary>
/// Reasons why a session might be dropped.
/// </summary>
public enum SessionDropReason
{
    /// <summary>
    /// Session was dropped due to timeout.
    /// </summary>
    Timeout,

    /// <summary>
    /// Session was dropped because the client disconnected.
    /// </summary>
    Disconnect,

    /// <summary>
    /// Session was dropped due to an explicit reset.
    /// </summary>
    Reset,

    /// <summary>
    /// Session was dropped because the agent crashed.
    /// </summary>
    Crash
}
