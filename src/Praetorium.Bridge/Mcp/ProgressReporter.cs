using System;
using System.Threading;

namespace Praetorium.Bridge.Mcp;

/// <summary>
/// Reports progress or keepalive signals during long-running agent operations.
/// </summary>
public class ProgressReporter : IDisposable
{
    private Timer? _timer;
    private readonly TimeSpan _interval;
    private readonly Action _onKeepAlive;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the ProgressReporter class.
    /// </summary>
    /// <param name="interval">The interval at which to send keepalive signals.</param>
    /// <param name="onKeepAlive">Callback action to invoke on each keepalive interval.</param>
    public ProgressReporter(TimeSpan interval, Action onKeepAlive)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentException("Interval must be positive", nameof(interval));

        _interval = interval;
        _onKeepAlive = onKeepAlive ?? throw new ArgumentNullException(nameof(onKeepAlive));
    }

    /// <summary>
    /// Starts the progress reporter, emitting keepalive signals at the configured interval.
    /// </summary>
    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ProgressReporter));

        if (_timer != null)
            return;

        _timer = new Timer(
            _ => _onKeepAlive(),
            null,
            _interval,
            _interval);
    }

    /// <summary>
    /// Stops the progress reporter from emitting keepalive signals.
    /// </summary>
    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>
    /// Disposes the progress reporter and releases its resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _disposed = true;
    }
}
