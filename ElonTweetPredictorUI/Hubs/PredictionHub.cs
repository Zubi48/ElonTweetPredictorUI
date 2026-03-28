using ElonTweetPredictorUI.Api;
using ElonTweetPredictorUI.Services;
using Microsoft.AspNetCore.SignalR;

namespace ElonTweetPredictorUI.Hubs;

public class PredictionHub : Hub
{
    private readonly IStatusService _statusService;

    public PredictionHub(IStatusService statusService)
    {
        _statusService = statusService;
    }

    /// <summary>
    /// Called by clients on connect to get the current snapshot immediately.
    /// </summary>
    public async Task RequestSnapshot()
    {
        var status = await _statusService.GetStatusAsync();
        if (status is not null)
            await Clients.Caller.SendAsync("PredictionUpdated", TradingPayload.Build(status));
    }
}
