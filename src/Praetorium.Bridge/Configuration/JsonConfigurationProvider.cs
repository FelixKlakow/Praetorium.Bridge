using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Praetorium.Bridge.Configuration;

/// <summary>
/// File-based configuration provider that reads from and writes to a JSON file with hot-reload support.
/// </summary>
public class JsonConfigurationProvider : IConfigurationProvider, IDisposable
{
    private readonly string _filePath;
    private readonly string _configDirectory;
    private readonly FileSystemWatcher? _watcher;
    private readonly object _lock = new();
    private BridgeConfiguration _configuration = new();
    private bool _disposed;
    private int _suppressWatcherCount;

    /// <summary>
    /// Initializes a new instance of the JsonConfigurationProvider class.
    /// </summary>
    /// <param name="filePath">Path to the JSON configuration file.</param>
    public JsonConfigurationProvider(string filePath)
    {
        if (filePath == null)
            throw new ArgumentNullException(nameof(filePath));

        _filePath = Path.GetFullPath(filePath);
        _configDirectory = Path.GetDirectoryName(_filePath) ?? Directory.GetCurrentDirectory();

        // Try to load existing configuration
        if (File.Exists(_filePath))
        {
            try
            {
                LoadFromFile();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load configuration from {_filePath}", ex);
            }
        }

        // Set up file watcher for hot-reload
        var directory = Path.GetDirectoryName(_filePath);
        var fileName = Path.GetFileName(_filePath);

        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            _watcher = new FileSystemWatcher(directory, fileName)
            {
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnFileChanged;
            _watcher.Renamed += OnFileRenamed;
        }
    }

    /// <inheritdoc />
    public BridgeConfiguration Configuration
    {
        get
        {
            lock (_lock)
            {
                return _configuration;
            }
        }
    }

    /// <inheritdoc />
    public string ConfigDirectory => _configDirectory;

    /// <inheritdoc />
    public event Action<BridgeConfiguration>? OnConfigurationChanged;

    /// <inheritdoc />
    public async Task ReloadAsync(CancellationToken ct)
    {
        await Task.Run(() =>
        {
            lock (_lock)
            {
                LoadFromFile();
                OnConfigurationChanged?.Invoke(_configuration);
            }
        }, ct);
    }

    /// <inheritdoc />
    public async Task SaveAsync(BridgeConfiguration config, CancellationToken ct)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        await Task.Run(() =>
        {
            lock (_lock)
            {
                var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                });

                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                Interlocked.Increment(ref _suppressWatcherCount);
                try
                {
                    File.WriteAllText(_filePath, json);
                }
                finally
                {
                    // Let any watcher events from this write drain before re-enabling.
                    Task.Delay(WatcherSuppressWindowMs).ContinueWith(_ => Interlocked.Decrement(ref _suppressWatcherCount));
                }
                _configuration = config;
            }
        }, ct);
    }

    /// <summary>
    /// Disposes the file system watcher and other resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _watcher?.Dispose();
        _disposed = true;
    }

    private void LoadFromFile()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        var json = File.ReadAllText(_filePath);
        var config = JsonSerializer.Deserialize<BridgeConfiguration>(json);

        if (config != null)
        {
            _configuration = config;
        }
    }

    private const int WatcherSuppressWindowMs = 500;

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        if (Volatile.Read(ref _suppressWatcherCount) > 0)
            return;

        // Debounce to avoid multiple rapid reloads
        Thread.Sleep(100);

        if (Volatile.Read(ref _suppressWatcherCount) > 0)
            return;

        try
        {
            ReloadAsync(CancellationToken.None).Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            // Log error but don't throw - we want to continue operating
            System.Diagnostics.Debug.WriteLine($"Failed to reload configuration: {ex.Message}");
        }
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (Volatile.Read(ref _suppressWatcherCount) > 0)
            return;

        try
        {
            ReloadAsync(CancellationToken.None).Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to reload configuration after rename: {ex.Message}");
        }
    }
}
