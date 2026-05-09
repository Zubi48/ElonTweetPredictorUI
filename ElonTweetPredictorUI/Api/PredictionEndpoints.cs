using ElonTweetPredictorUI.Services;

namespace ElonTweetPredictorUI.Api;

public static class PredictionEndpoints
{
    public static void MapPredictionApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        // Full snapshot — same shape as the WebSocket PredictionUpdated event
        api.MapGet("/status", async Task<IResult> (
            IStatusService statusService,
            ISleepService sleepService,
            ITweetHeatmapService heatmapService) =>
        {
            var statusTask  = statusService.GetStatusAsync();
            var sleepTask   = sleepService.GetSleepDataAsync();
            var heatmapTask = heatmapService.GetHeatmapAsync(7);
            await Task.WhenAll(statusTask, sleepTask, heatmapTask);

            var status = statusTask.Result;
            if (status is null) return Results.NotFound();

            return Results.Ok(TradingPayload.Build(status, sleepTask.Result, heatmapTask.Result));
        });

        // Slim endpoint: only the interval forecasts (unchanged for backward compat)
        api.MapGet("/bet-interval-forecasts", async Task<IResult> (IStatusService statusService) =>
        {
            var status = await statusService.GetStatusAsync();
            if (status is null) return Results.NotFound();
            return Results.Ok(status.BetIntervalForecasts);
        });

        // Bot-focused context: sleep + tweet-activity signals only (no heavy prediction data)
        api.MapGet("/bot-context", async Task<IResult> (
            ISleepService sleepService,
            ITweetHeatmapService heatmapService) =>
        {
            var sleepTask   = sleepService.GetSleepDataAsync();
            var heatmapTask = heatmapService.GetHeatmapAsync(7);
            await Task.WhenAll(sleepTask, heatmapTask);

            // Reuse the same builder with a dummy status — but extract only the new sections
            // by calling the dedicated helpers through a minimal object
            return Results.Ok(TradingPayload.BuildBotContext(sleepTask.Result, heatmapTask.Result));
        });
    }
}
