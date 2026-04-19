namespace Praetorium.Bridge.Configuration;

/// <summary>
/// Defines how sessions are managed in the bridge.
/// </summary>
public enum SessionMode
{
    /// <summary>
    /// One session per connection.
    /// </summary>
    PerConnection,

    /// <summary>
    /// One session per reference ID.
    /// </summary>
    PerReference,

    /// <summary>
    /// Single global session for all connections.
    /// </summary>
    Global
}
