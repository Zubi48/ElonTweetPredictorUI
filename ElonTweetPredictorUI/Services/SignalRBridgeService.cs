using ElonTweetPredictorUI.Api;
using ElonTweetPredictorUI.Hubs;
using ElonTweetPredictorUI.Services;
using Microsoft.AspNetCore.SignalR;

namespace ElonTweetPredictorUI.Services;

/// <summary>
/// Listens for file changes via <see cref="IDataChangeNotifier"/> and pushes
/// updated data to all connected SignalR clients.
/// </summary>
public sealed class SignalRBridgeService : BackgroundService, IDisposable
{
    private readonly IDataChangeNotifier _notifier;
    private readonly IHubContext<PredictionHub> _hubContext;
    private readonly IStatusService _statusService;
    private readonly ILogger<SignalRBridgeService> _logger;

    public SignalRBridgeService(
        IDataChangeNotifier notifier,
        IHubContext<PredictionHub> hubContext,
        IStatusService statusService,
        ILogger<SignalRBridgeService> logger)
    {
        _notifier = notifier;
        _hubContext = hubContext;
        _statusService = statusService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _notifier.DataChanged += OnDataChangedAsync;

        while (!stoppingToken.IsCancellationRequested)
        {
            await PushUpdateAsync();
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task OnDataChangedAsync()
    {
        await PushUpdateAsync();
    }

    private async Task PushUpdateAsync()
    {
        try
        {
            var status = await _statusService.GetStatusAsync();
            if (status is not null)
                await _hubContext.Clients.All.SendAsync("PredictionUpdated", TradingPayload.Build(status));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push prediction update via SignalR.");
        }
    }

    public override void Dispose()
    {
        _notifier.DataChanged -= OnDataChangedAsync;
        base.Dispose();
    }
}
