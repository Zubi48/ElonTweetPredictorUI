namespace ElonTweetPredictorUI.Services;

public interface IDataChangeNotifier
{
    event Func<Task>? DataChanged;
}

public sealed class DataChangeNotifier : IDataChangeNotifier, IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly object _debounceLock = new();
    private Timer? _debounceTimer;

    public event Func<Task>? DataChanged;

    public DataChangeNotifier(IConfiguration configuration, ILogger<DataChangeNotifier> logger)
    {
        var dataPath = configuration["DataPath"] ?? ".";

        if (!Directory.Exists(dataPath))
        {
            logger.LogWarning("Data path '{DataPath}' does not exist. Live file updates are disabled.", dataPath);
            return;
        }

        _watcher = new FileSystemWatcher(dataPath)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += OnRenamed;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (!ShouldNotify(e.Name))
        {
            return;
        }

        ScheduleNotification();
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (!ShouldNotify(e.Name) && !ShouldNotify(e.OldName))
        {
            return;
        }

        ScheduleNotification();
    }

    private static bool ShouldNotify(string? fileName)
    {
        return fileName is not null &&
               (fileName.Equals("status.json", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("logs.json", StringComparison.OrdinalIgnoreCase));
    }

    private void ScheduleNotification()
    {
        lock (_debounceLock)
        {
            _debounceTimer ??= new Timer(_ => _ = NotifySubscribersAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _debounceTimer.Change(TimeSpan.FromMilliseconds(250), Timeout.InfiniteTimeSpan);
        }
    }

    private async Task NotifySubscribersAsync()
    {
        var handlers = DataChanged;
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Func<Task>>())
        {
            await handler();
        }
    }

    public void Dispose()
    {
        if (_watcher is not null)
        {
            _watcher.Changed -= OnChanged;
            _watcher.Created -= OnChanged;
            _watcher.Renamed -= OnRenamed;
            _watcher.Dispose();
        }

        _debounceTimer?.Dispose();
    }
}
