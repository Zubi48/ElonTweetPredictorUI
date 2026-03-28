using ElonTweetPredictorUI.Services;

namespace ElonTweetPredictorUI.Api;

public static class PredictionEndpoints
{
    public static void MapPredictionApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/status", async Task<IResult> (IStatusService statusService) =>
        {
            var status = await statusService.GetStatusAsync();
            if (status is null)
                return Results.NotFound();

            return Results.Ok(TradingPayload.Build(status));
        });

        api.MapGet("/bet-interval-forecasts", async Task<IResult> (IStatusService statusService) =>
        {
            var status = await statusService.GetStatusAsync();
            if (status is null)
                return Results.NotFound();

            return Results.Ok(status.BetIntervalForecasts);
        });
    }
}
