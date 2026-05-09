using ElonTweetPredictorUI.Api;
using ElonTweetPredictorUI.Services;
using Microsoft.AspNetCore.SignalR;

namespace ElonTweetPredictorUI.Hubs;

public class PredictionHub : Hub
{
    private readonly IStatusService _statusService;
    private readonly ISleepService _sleepService;
    private readonly ITweetHeatmapService _heatmapService;

    public PredictionHub(
        IStatusService statusService,
        ISleepService sleepService,
        ITweetHeatmapService heatmapService)
    {
        _statusService  = statusService;
        _sleepService   = sleepService;
        _heatmapService = heatmapService;
    }

    /// <summary>
    /// Called by clients on connect to get the current snapshot immediately.
    /// </summary>
    public async Task RequestSnapshot()
    {
        var statusTask  = _statusService.GetStatusAsync();
        var sleepTask   = _sleepService.GetSleepDataAsync();
        var heatmapTask = _heatmapService.GetHeatmapAsync(7);
        await Task.WhenAll(statusTask, sleepTask, heatmapTask);

        var status = statusTask.Result;
        if (status is not null)
            await Clients.Caller.SendAsync(
                "PredictionUpdated",
                TradingPayload.Build(status, sleepTask.Result, heatmapTask.Result));
    }
}
