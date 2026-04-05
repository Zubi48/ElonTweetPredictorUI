namespace ElonTweetPredictorUI.Services;

public interface ITradingChangeNotifier
{
    event Func<Task>? TradingDataChanged;
}

public sealed class TradingChangeNotifier : ITradingChangeNotifier, IDisposable
{
    private readonly FileSystemWatcher? _watcher;
    private readonly object _debounceLock = new();
    private Timer? _debounceTimer;

    public event Func<Task>? TradingDataChanged;

    public TradingChangeNotifier(IConfiguration configuration, ILogger<TradingChangeNotifier> logger)
    {
        var dataPath = configuration["DataPath"] ?? ".";

        if (!Directory.Exists(dataPath))
        {
            logger.LogWarning("Data path '{DataPath}' does not exist. Trading live updates are disabled.", dataPath);
            return;
        }

        _watcher = new FileSystemWatcher(dataPath)
        {
            Filter = "trades.log",
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
