using System;
using System.Threading;
using ModelContextProtocol;

namespace Praetorium.Bridge.Mcp;

/// <summary>
/// Emits periodic <see cref="ProgressNotificationValue"/> keepalives to an
/// <see cref="IProgress{T}"/> while one side of the bridge is blocked waiting on the
/// other. Used both by the dispatcher (external caller blocked while the agent works)
/// and by blocking signaling tools (agent blocked while the external caller works).
/// </summary>
public sealed class ProgressReporter : IDisposable
{
    private readonly IProgress<ProgressNotificationValue> _progress;
    private readonly TimeSpan _interval;
    private readonly string? _message;
    private Timer? _timer;
    private int _tick;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the ProgressReporter class.
    /// </summary>
    /// <param name="progress">The sink that receives each keepalive notification.</param>
    /// <param name="interval">The interval at which keepalives are emitted. Must be positive.</param>
    /// <param name="message">Optional static message attached to each notification.</param>
    public ProgressReporter(
        IProgress<ProgressNotificationValue> progress,
        TimeSpan interval,
        string? message = null)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentException("Interval must be positive.", nameof(interval));

        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _interval = interval;
        _message = message;
    }

    /// <summary>
    /// Starts the reporter. Subsequent calls are no-ops.
    /// </summary>
    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ProgressReporter));

        if (_timer != null)
            return;

        _timer = new Timer(OnTick, null, _interval, _interval);
    }

    /// <summary>
    /// Stops the reporter. Safe to call multiple times.
    /// </summary>
    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _disposed = true;
    }

    private void OnTick(object? _)
    {
        try
        {
            var tick = Interlocked.Increment(ref _tick);
            _progress.Report(new ProgressNotificationValue
            {
                Progress = tick,
                Message = _message,
            });
        }
        catch
        {
            // Progress reporting must never break the caller.
        }
    }
}

