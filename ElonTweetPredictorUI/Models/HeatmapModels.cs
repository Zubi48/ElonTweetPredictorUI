namespace ElonTweetPredictorUI.Models;

public class HeatmapDayColumn
{
    /// <summary>e.g. "Sat", "Sun"</summary>
    public string DayLabel { get; set; } = "";
    /// <summary>Day-of-month number, e.g. 2</summary>
    public int DayNumber { get; set; }
    /// <summary>Full date for sleep overlay lookup</summary>
    public DateOnly Date { get; set; }
    /// <summary>Tweet counts indexed by hour 0-23</summary>
    public int[] HourCounts { get; set; } = new int[24];
    /// <summary>Total tweets that day</summary>
    public int Total => HourCounts.Sum();
    /// <summary>True if this is today (EST)</summary>
    public bool IsToday { get; set; }
}

public class HeatmapData
{
    /// <summary>Columns from oldest to newest (last N days)</summary>
    public List<HeatmapDayColumn> Days { get; set; } = [];
    /// <summary>Average tweets per hour across all history (index = hour 0-23)</summary>
    public double[] HourlyAvg { get; set; } = new double[24];
    /// <summary>Max count in any single cell (for colour scaling)</summary>
    public int MaxCount { get; set; }
    /// <summary>Hour 0-23 in EST right now</summary>
    public int CurrentHour { get; set; }
    /// <summary>Set of (date, hour) pairs that fall inside a recorded sleep window</summary>
    public HashSet<(DateOnly, int)> SleepingCells { get; set; } = [];
}
