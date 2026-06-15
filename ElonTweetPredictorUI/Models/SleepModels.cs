namespace ElonTweetPredictorUI.Models;

/// <summary>
/// Per-weekday sleep summary parsed from the v4 report. Captured for both
/// LAUNCH and NON-LAUNCH day groups (distinguished by <see cref="IsLaunch"/>).
/// </summary>
public class WeekdaySleepSummary
{
    public string Weekday { get; set; } = "";       // e.g. "Monday"
    public bool IsLaunch { get; set; }
    public int Nights { get; set; }
    public string AvgBedtime { get; set; } = "";    // e.g. "02:28 AM"
    public string AvgWakeUp { get; set; } = "";     // e.g. "11:31 AM"
    public double AvgSleepHours { get; set; }
    public double MinSleepHours { get; set; }
    public double MaxSleepHours { get; set; }
    // v4 additions
    public string EarliestBed { get; set; } = "";   // e.g. "02:28 AM EST (2026-02-03)"
    public string LatestBed { get; set; } = "";
    public string EarliestWake { get; set; } = "";
    public string LatestWake { get; set; } = "";
    public double AvgBedSessionTweets { get; set; }
    public double AvgMornSessionTweets { get; set; }
}

/// <summary>
/// One "down-for-the-night confirmation" row: how much silence after a tweet
/// at <see cref="LastTweet"/> is needed to reach each probability target.
/// </summary>
public class ConfirmationThreshold
{
    public string Weekday { get; set; } = "";       // e.g. "Friday"
    public string LastTweet { get; set; } = "";     // e.g. "11:00 PM"
    public string Target80 { get; set; } = "";      // e.g. "3h45m"
    public string Target90 { get; set; } = "";
    public string Target95 { get; set; } = "";
}

/// <summary>
/// The headline "CURRENT SLEEP-STATE ESTIMATE (v4)" block - the single most
/// useful piece of the report for live decisions.
/// </summary>
public class CurrentSleepEstimate
{
    public string NowEst { get; set; } = "";            // "Saturday 2026-06-13 01:00 AM"
    public string LastTweetEst { get; set; } = "";      // "Friday 2026-06-12 10:15 PM"
    public string SilenceSoFar { get; set; } = "";      // "2h45m"
    public string ClockRegime { get; set; } = "";       // "NIGHT - inside sleep zone (...)"
    public string ActivityTier { get; set; } = "";      // "MID" (from "34 tweets -> tier MID")

    public double AsleepProbability { get; set; }       // 76.4
    public double AsleepLow { get; set; }               // 59.1
    public double AsleepHigh { get; set; }              // 90.1

    public double NoTweetsUntil5Probability { get; set; } // 56.0
    public double NoTweets5Low { get; set; }              // 37.4
    public double NoTweets5High { get; set; }             // 73.6

    public string NextTweetMedian { get; set; } = "";        // "02:18 AM EST (Sat 06/13)"
    public string NextTweet50Interval { get; set; } = "";    // "01:20 AM - 03:10 AM EST (...)"
    public string NextTweet90Pct { get; set; } = "";         // "05:23 AM EST (Sat 06/13)"

    public string BranchIfTweetsAgain { get; set; } = "";    // "~ 01:26 AM EST (Sat 06/13)"
    public string BranchIfSilentTillMorning { get; set; } = "";
    public string BranchWeightedMean { get; set; } = "";

    // Precision probabilities: P(next tweet within ±N min of median)
    public double Within15MinPct { get; set; }
    public double Within30MinPct { get; set; }
    public double Within60MinPct { get; set; }

    // Hour-by-hour next-tweet probability distribution
    public List<(string Hour, double Pct)> HourlyProbabilities { get; set; } = [];

    public double AwakeProbability => Math.Round(100.0 - AsleepProbability, 1);

    /// <summary>UTC time the Python script ran the analysis (parsed from NowEst).</summary>
    public DateTime? AnalysisUtc { get; set; }

    /// <summary>How old the analysis is relative to now. Null if AnalysisUtc is unavailable.</summary>
    public TimeSpan? DataAge => AnalysisUtc.HasValue ? DateTime.UtcNow - AnalysisUtc.Value : null;
}

public class SleepData
{
    public CurrentSleepEstimate? CurrentEstimate { get; set; }
    public List<WeekdaySleepSummary> WeekdaySummaries { get; set; } = [];
    public List<ConfirmationThreshold> Thresholds { get; set; } = [];
    public DateTime ParsedAt { get; set; }
}
