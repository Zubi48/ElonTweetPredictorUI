namespace ElonTweetPredictorUI.Models;

public class SleepPeriod
{
    public DateOnly Date { get; set; }
    public string Weekday { get; set; } = "";
    public string BedtimeStr { get; set; } = "";   // e.g. "02:39 AM EST"
    public string WakeTimeStr { get; set; } = "";  // e.g. "08:46 AM EST"
    public double DurationHours { get; set; }
    public int BedTweets { get; set; }
    public int MornTweets { get; set; }

    /// <summary>Bedtime as TimeOnly (parsed, hour in 0-23).</summary>
    public TimeOnly Bedtime { get; set; }
    /// <summary>Wake time as TimeOnly (parsed, hour in 0-23).</summary>
    public TimeOnly WakeTime { get; set; }
}

public class WeekSummary
{
    public string WeekLabel { get; set; } = "";   // e.g. "2026-W17"
    public int Nights { get; set; }
    public string Weekdays { get; set; } = "";
    public string AvgBedtime { get; set; } = "";
    public string AvgWakeUp { get; set; } = "";
    public double AvgSleepHours { get; set; }
}

public class SleepExtremes
{
    public string LatestBedtime { get; set; } = "";
    public string EarliestBedtime { get; set; } = "";
    public string EarliestWakeUp { get; set; } = "";
    public string LatestWakeUp { get; set; } = "";
    public string LongestSleep { get; set; } = "";
    public string ShortestSleep { get; set; } = "";
}

public class SleepData
{
    public int TotalSleepPeriods { get; set; }
    public List<SleepPeriod> Periods { get; set; } = [];
    public List<WeekSummary> WeekSummaries { get; set; } = [];
    public SleepExtremes Extremes { get; set; } = new();
    public DateTime ParsedAt { get; set; }
}
