using System;

namespace Praetorium.Bridge.Hooks;

/// <summary>
/// Base class for all bridge hook contexts, providing common metadata.
/// </summary>
public class BridgeHookContext
{
    /// <summary>
    /// Initializes a new instance of the BridgeHookContext class.
    /// </summary>
    /// <param name="correlationId">A unique identifier that correlates related events.</param>
    public BridgeHookContext(string correlationId)
    {
        CorrelationId = correlationId ?? throw new ArgumentNullException(nameof(correlationId));
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the timestamp when this event occurred.
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// Gets the correlation ID that relates this event to others.
    /// </summary>
    public string CorrelationId { get; }
}
