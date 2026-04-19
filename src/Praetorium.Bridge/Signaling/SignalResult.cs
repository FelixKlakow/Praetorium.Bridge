namespace Praetorium.Bridge.Signaling;

/// <summary>
/// Represents the result of a signal delivery operation.
/// </summary>
public class SignalResult
{
    /// <summary>
    /// Initializes a new instance of the SignalResult class.
    /// </summary>
    /// <param name="type">The type of signal.</param>
    /// <param name="data">Optional payload data associated with the signal.</param>
    public SignalResult(SignalType type, object? data = null)
    {
        Type = type;
        Data = data;
    }

    /// <summary>
    /// Gets the type of signal.
    /// </summary>
    public SignalType Type { get; }

    /// <summary>
    /// Gets the optional payload data associated with the signal.
    /// </summary>
    public object? Data { get; }

    /// <summary>
    /// Creates an Input signal result with the given data payload.
    /// </summary>
    /// <param name="data">The input data payload.</param>
    /// <returns>A new SignalResult representing an Input signal.</returns>
    public static SignalResult Input(object data) => new(SignalType.Input, data);

    /// <summary>
    /// Creates a Disconnect signal result.
    /// </summary>
    /// <returns>A new SignalResult representing a Disconnect signal.</returns>
    public static SignalResult Disconnect() => new(SignalType.Disconnect);

    /// <summary>
    /// Creates a Reset signal result.
    /// </summary>
    /// <returns>A new SignalResult representing a Reset signal.</returns>
    public static SignalResult Reset() => new(SignalType.Reset);

    /// <summary>
    /// Creates a Timeout signal result.
    /// </summary>
    /// <returns>A new SignalResult representing a Timeout signal.</returns>
    public static SignalResult Timeout() => new(SignalType.Timeout);
}
