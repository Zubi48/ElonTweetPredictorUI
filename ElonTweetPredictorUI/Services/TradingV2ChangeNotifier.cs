namespace ElonTweetPredictorUI.Services;

public interface ITradingV2ChangeNotifier
{
    event Func<Task>? TradingDataChanged;
}

public sealed class TradingV2ChangeNotifier : ITradingV2ChangeNotifier, IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly object _debounceLock = new();
    private Timer? _debounceTimer;

    public event Func<Task>? TradingDataChanged;

    public TradingV2ChangeNotifier(IConfiguration configuration, ILogger<TradingV2ChangeNotifier> logger)
    {
        var dataPath = configuration["DataPathV2"] ?? configuration["DataPath"] ?? ".";

        if (!Directory.Exists(dataPath))
        {
            logger.LogWarning("Data path '{DataPath}' does not exist. Trading v2 live updates are disabled.", dataPath);
            return;
        }

        _watcher = new FileSystemWatcher(dataPath)
        {
            Filter = "unified-trades.log",
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        lock (_debounceLock)
        {
            _debounceTimer ??= new Timer(_ => _ = NotifySubscribersAsync(), null,
                Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _debounceTimer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
        }
    }

    private async Task NotifySubscribersAsync()
    {
        var handlers = TradingDataChanged;
        if (handlers is null)
            return;

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
            _watcher.Dispose();
        }

        _debounceTimer?.Dispose();
    }
}
