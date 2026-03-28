using ElonTweetPredictorUI.Models;

namespace ElonTweetPredictorUI.Api;

public static class TradingPayload
{
    /// <summary>
    /// Builds a lean payload containing only the data a trading bot needs.
    /// </summary>
    public static object Build(PredictorStatus status) => new
    {
        status.UpdatedAt,
        status.TweetsThisWeek,
        Prediction = new
        {
            status.Prediction.BayesianTotal,
            status.Prediction.CiLower,
            status.Prediction.CiUpper,
            status.Prediction.DaysObserved,
            status.Prediction.DaysRemaining,
            status.Prediction.Pace
        },
        Forecasts = status.BetIntervalForecasts.Select(f => new
        {
            f.Title,
            f.TimeRemaining,
            f.TweetsInWindow,
            f.PredictedTotal,
            f.CiLower,
            f.CiUpper,
            f.Sigma,
            f.Intervals
        })
    };
}
