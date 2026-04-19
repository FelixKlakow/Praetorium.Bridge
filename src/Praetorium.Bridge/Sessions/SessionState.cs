namespace Praetorium.Bridge.Sessions;

/// <summary>
/// Enumeration of possible session states.
/// </summary>
public enum SessionState
{
    /// <summary>
    /// Session is being spawned and initializing.
    /// </summary>
    Spawning,

    /// <summary>
    /// Session is active and running.
    /// </summary>
    Active,

    /// <summary>
    /// Session is currently responding to input or processing.
    /// </summary>
    Responding,

    /// <summary>
    /// Session is pooled and waiting for reuse.
    /// </summary>
    Pooled,

    /// <summary>
    /// Session is waiting for input from the caller.
    /// </summary>
    WaitingInput,

    /// <summary>
    /// Session has crashed and is no longer viable.
    /// </summary>
    Crashed
}
