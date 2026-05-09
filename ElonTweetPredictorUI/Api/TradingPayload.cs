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
        Forecasts = status.BetIntervalForecasts.Select(f => new
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
            })
        }),
        SleepContext  = BuildSleepContext(sleepData),
        TweetActivity = BuildTweetActivity(heatmap),
    };

    // ── Sleep context ─────────────────────────────────────────────────────────

    private static object BuildSleepContext(SleepData? data)
    {
        if (data is null || data.Periods.Count == 0)
            return new { available = false };

        var estNow   = TimeZoneInfo.ConvertTime(DateTime.UtcNow, Est);
        var nowTime  = TimeOnly.FromDateTime(estNow);

        // Most-recent sleep period
        var last = data.Periods
            .OrderByDescending(p => p.Date)
            .First();

        // Is Elon likely asleep right now?
        // Use the rolling 7-night average sleep window projected onto today.
        // This always produces a result regardless of how stale the last record is.
        // We compute it after the rolling avg is built below, so defer with a local func.

        // Average bedtime/wake from last 7 nights
        var recent = data.Periods
            .OrderByDescending(p => p.Date)
            .Take(7)
            .ToList();

        var avgBedMins  = recent.Average(p => ToMinutes(p.Bedtime));
        var avgWakeMins = recent.Average(p => ToMinutes(p.WakeTime));
        var avgDuration = recent.Average(p => p.DurationHours);

        // Expected tonight window (uses 7-night rolling avg)
        var expectedBedtime  = MinutesToTimeOnly((int)avgBedMins);
        var expectedWakeTime = MinutesToTimeOnly((int)avgWakeMins);

        // Time until expected bedtime / wake
        double hoursUntilBed  = HoursUntil(nowTime, expectedBedtime);
        double hoursUntilWake = HoursUntil(nowTime, expectedWakeTime);

        // Derive is_asleep_now from the rolling-avg window (always available)
        // Falls back to last known period if we have a same/previous-day record
        bool isAsleepNow = IsDuringWindow(nowTime, expectedBedtime, expectedWakeTime);

        return new
        {
            available         = true,
            is_asleep_now     = isAsleepNow,
            is_asleep_based_on = "rolling_7_night_avg",
            last_known = new
            {
                date          = last.Date.ToString("yyyy-MM-dd"),
                bedtime       = last.BedtimeStr,
                wake_time     = last.WakeTimeStr,
                duration_hours = last.DurationHours
            },
            rolling_7_night_avg = new
            {
                bedtime        = expectedBedtime.ToString("hh:mm tt") + " EST",
                wake_time      = expectedWakeTime.ToString("hh:mm tt") + " EST",
                duration_hours = Math.Round(avgDuration, 1)
            },
            hours_until_expected_bedtime  = Math.Round(hoursUntilBed,  1),
            hours_until_expected_wakeup   = Math.Round(hoursUntilWake, 1),
            // Actionable signal for the bot
            activity_signal = DeriveActivitySignal(isAsleepNow, hoursUntilBed, hoursUntilWake)
        };
    }

    private static string DeriveActivitySignal(bool asleep, double hBed, double hWake)
    {
        if (asleep)          return "SLEEPING — expect very low tweet volume";
        if (hBed  < 1.0)     return "APPROACHING_SLEEP — activity likely declining";
        if (hWake < 2.0)     return "JUST_WOKE — activity likely ramping up";
        return "AWAKE — normal activity expected";
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
    private static bool IsDuringWindow(TimeOnly t, TimeOnly bed, TimeOnly wake)
        => bed <= wake
            ? t >= bed && t < wake
            : t >= bed || t < wake;

    private static int ToMinutes(TimeOnly t) => t.Hour * 60 + t.Minute;

    private static TimeOnly MinutesToTimeOnly(int mins)
    {
        // Keep in 0-1439 range
        mins = ((mins % 1440) + 1440) % 1440;
        return new TimeOnly(mins / 60, mins % 60);
    }

    private static double HoursUntil(TimeOnly from, TimeOnly target)
    {
        var diff = target.ToTimeSpan() - from.ToTimeSpan();
        if (diff < TimeSpan.Zero) diff += TimeSpan.FromHours(24);
        return diff.TotalHours;
    }
}

