using ElonTweetPredictorUI.Models;

namespace ElonTweetPredictorUI.Api;

public static class TradingPayload
{
    private static readonly TimeZoneInfo Est =
        TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    /// <summary>
    /// Builds the full trading payload including prediction, forecasts,
    /// sleep context and tweet-activity signals.
    /// </summary>
    public static object Build(
        PredictorStatus status,
        SleepData? sleepData = null,
        HeatmapData? heatmap = null) => new
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
        // Hawkes model interval forecasts (the legacy v4 Forecasts field was removed).
        HawkesForecasts = status.HawkesBetIntervalForecasts.Select(f => new
        {
            f.Title,
            f.TimeRemaining,
            f.TweetsInWindow,
            f.PredictedTotal,
            f.CiLower,
            f.CiUpper,
            f.Sigma,
            Intervals = f.Intervals.Select(i => new
            {
                i.Label,
                i.Probability,
                i.IsPredicted
            }),
            f.WindowRisk,
            f.RegimeContext,
            f.Stability
        }),
        SleepContext  = BuildSleepContext(sleepData),
        TweetActivity = BuildTweetActivity(heatmap),
    };

    // ── Sleep context ─────────────────────────────────────────────────────────

    private static object BuildSleepContext(SleepData? data)
    {
        if (data?.CurrentEstimate is not { } est)
            return new { available = false };

        var estNow = TimeZoneInfo.ConvertTime(DateTime.UtcNow, Est);

        // Weekday summary that matches the night's starting day, preferring
        // non-launch (the common case) when both groups are present.
        var weekdayName = ExtractWeekday(est.ClockRegime, est.NowEst) ?? estNow.DayOfWeek.ToString();
        var weekday = data.WeekdaySummaries
            .Where(w => w.Weekday.Equals(weekdayName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(w => w.IsLaunch ? 1 : 0)
            .FirstOrDefault();

        return new
        {
            available = true,
            is_asleep_now = est.AsleepProbability >= 50,
            p_asleep_now_pct = est.AsleepProbability,
            p_asleep_band = new { low = est.AsleepLow, high = est.AsleepHigh },
            p_no_tweets_until_5am_pct = est.NoTweetsUntil5Probability,
            p_no_tweets_until_5am_band = new { low = est.NoTweets5Low, high = est.NoTweets5High },
            now_est = est.NowEst,
            last_tweet_est = est.LastTweetEst,
            silence_so_far = est.SilenceSoFar,
            clock_regime = est.ClockRegime,
            activity_tier = est.ActivityTier,
            next_tweet = new
            {
                median = est.NextTweetMedian,
                interval_50 = est.NextTweet50Interval,
                pct_90 = est.NextTweet90Pct,
                if_tweets_again = est.BranchIfTweetsAgain,
                if_silent_until_morning = est.BranchIfSilentTillMorning,
                weighted_mean = est.BranchWeightedMean
            },
            weekday_profile = weekday is null ? null : new
            {
                weekday = weekday.Weekday,
                nights = weekday.Nights,
                avg_bedtime = weekday.AvgBedtime,
                avg_wakeup = weekday.AvgWakeUp,
                avg_sleep_hours = weekday.AvgSleepHours
            },
            activity_signal = DeriveActivitySignal(est)
        };
    }

    private static string DeriveActivitySignal(CurrentSleepEstimate e)
    {
        if (e.AsleepProbability >= 80) return "SLEEPING - expect very low tweet volume";
        if (e.AsleepProbability >= 55) return "LIKELY_SLEEPING - activity probably suppressed";
        if (e.AsleepProbability >= 45) return "UNCERTAIN - could go either way";
        return "AWAKE - normal activity expected";
    }

    /// <summary>Pull a weekday name out of the v2 estimate text (e.g. "Saturday 2026-06-13 ...").</summary>
    private static string? ExtractWeekday(params string[] sources)
    {
        string[] days = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];
        foreach (var s in sources)
            foreach (var d in days)
                if (!string.IsNullOrEmpty(s) && s.Contains(d, StringComparison.OrdinalIgnoreCase))
                    return d;
        return null;
    }
    // ── Tweet activity / heatmap signals ─────────────────────────────────────

    private static object BuildTweetActivity(HeatmapData? hm)
    {
        if (hm is null)
            return new { available = false };

        var estNow    = TimeZoneInfo.ConvertTime(DateTime.UtcNow, Est);
        var curHour   = estNow.Hour;
        var curHourAvg = hm.HourlyAvg[curHour];
        var overallAvg = hm.HourlyAvg.Average();

        // Top 3 peak hours
        var top3 = hm.HourlyAvg
            .Select((avg, h) => new { hour = h, avg = Math.Round(avg, 2) })
            .OrderByDescending(x => x.avg)
            .Take(3)
            .ToList();

        // Best 3-hour bet window (highest cumulative avg)
        int bestStart = 0;
        double bestSum = -1;
        for (int h = 0; h <= 21; h++)
        {
            var s = hm.HourlyAvg[h] + hm.HourlyAvg[h + 1] + hm.HourlyAvg[h + 2];
            if (s > bestSum) { bestSum = s; bestStart = h; }
        }

        // Recent trend: last 2 days vs prior 2 days
        string trend      = "insufficient_data";
        double trendPct   = 0;
        if (hm.Days.Count >= 4)
        {
            var recent  = hm.Days.TakeLast(2).Average(d => (double)d.Total);
            var earlier = hm.Days.SkipLast(2).TakeLast(2).Average(d => (double)d.Total);
            if (earlier > 0)
            {
                trendPct = (recent - earlier) / earlier * 100;
                trend    = trendPct >= 5 ? "increasing" : trendPct <= -5 ? "decreasing" : "stable";
            }
        }

        // Activity level bucket
        var totalPerDay = hm.HourlyAvg.Sum();
        var level = totalPerDay >= 30 ? "HIGH" : totalPerDay >= 15 ? "MEDIUM" : "LOW";

        return new
        {
            available = true,
            window_days = hm.Days.Count,
            current_hour = new
            {
                hour    = curHour,
                label   = $"{curHour:D2}:00 EST",
                avg_tweets = Math.Round(curHourAvg, 2),
                vs_hourly_mean = Math.Round(curHourAvg - overallAvg, 2)
            },
            peak_hours = top3.Select(x => new
            {
                hour  = x.hour,
                label = $"{x.hour:D2}:00 EST",
                avg_tweets = x.avg
            }),
            best_bet_window = new
            {
                start_hour = bestStart,
                end_hour   = bestStart + 3,
                label      = $"{bestStart:D2}:00–{bestStart + 3:D2}:00 EST",
                avg_tweets = Math.Round(bestSum, 1)
            },
            trend = new
            {
                direction  = trend,
                pct_change = Math.Round(trendPct, 1)
            },
            activity_level = level,
            hourly_avg = hm.HourlyAvg
                .Select((avg, h) => new { hour = h, label = $"{h:D2}:00", avg_tweets = Math.Round(avg, 2) })
                .ToList()
        };
    }

    /// <summary>
    /// Slim payload for GET /api/bot-context — only sleep + tweet-activity signals.
    /// </summary>
    public static object BuildBotContext(SleepData? sleepData, HeatmapData? heatmap) => new
    {
        generated_at   = TimeZoneInfo.ConvertTime(DateTime.UtcNow, Est).ToString("yyyy-MM-dd HH:mm:ss") + " EST",
        SleepContext   = BuildSleepContext(sleepData),
        TweetActivity  = BuildTweetActivity(heatmap),
    };

    // ── Helpers ───────────────────────────────────────────────────────────────
    }

