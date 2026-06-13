using System.Text.Json.Serialization;

namespace ElonTweetPredictorUI.Models;

public class PredictorStatus
{
    [JsonPropertyName("updated_at")]        public string          UpdatedAt        { get; set; } = "";
    [JsonPropertyName("state")]             public string          State            { get; set; } = "";
    [JsonPropertyName("cumulative_count")]  public int             CumulativeCount  { get; set; }
    [JsonPropertyName("tweets_this_week")]  public int             TweetsThisWeek   { get; set; }
    [JsonPropertyName("prediction")]        public PredictionInfo  Prediction       { get; set; } = new();
    [JsonPropertyName("active_trackings")]  public List<ActiveTracking> ActiveTrackings { get; set; } = [];
    [JsonPropertyName("model")]                  public ModelInfo       Model                { get; set; } = new();
    [JsonPropertyName("files")]                  public FilesInfo       Files                { get; set; } = new();
    [JsonPropertyName("temporal_patterns")]       public TemporalPatterns TemporalPatterns    { get; set; } = new();
    [JsonPropertyName("bet_interval_forecasts")]        public List<BetIntervalForecast> BetIntervalForecasts        { get; set; } = [];
    [JsonPropertyName("hawkes_bet_interval_forecasts")] public List<BetIntervalForecast> HawkesBetIntervalForecasts { get; set; } = [];
}

public class PredictionInfo
{
    [JsonPropertyName("weekly_total")]          public int    WeeklyTotal          { get; set; }
    [JsonPropertyName("bayesian_total")]        public int    BayesianTotal        { get; set; }
    [JsonPropertyName("ci_lower")]              public int    CiLower              { get; set; }
    [JsonPropertyName("ci_upper")]              public int    CiUpper              { get; set; }
    [JsonPropertyName("event_factor")]          public double EventFactor          { get; set; }
    [JsonPropertyName("event_adjustment_pct")]  public double EventAdjustmentPct   { get; set; }
    [JsonPropertyName("days_observed")]         public int    DaysObserved         { get; set; }
    [JsonPropertyName("days_remaining")]        public int    DaysRemaining        { get; set; }
    [JsonPropertyName("posterior_mean")]        public double PosteriorMean        { get; set; }
    [JsonPropertyName("posterior_std")]         public double PosteriorStd         { get; set; }
    [JsonPropertyName("pace")]                  public int?   Pace                 { get; set; }
}

public class ActiveTracking
{
    [JsonPropertyName("id")]               public string Id            { get; set; } = "";
    [JsonPropertyName("title")]            public string Title         { get; set; } = "";
    [JsonPropertyName("target")]           public int?   Target        { get; set; }
    [JsonPropertyName("startDate")]        public string StartDate     { get; set; } = "";
    [JsonPropertyName("endDate")]          public string EndDate       { get; set; } = "";
    [JsonPropertyName("tweets_in_window")] public int    TweetsInWindow { get; set; }
}

public class ModelInfo
{
    [JsonPropertyName("prior_mean")]      public double PriorMean     { get; set; }
    [JsonPropertyName("prior_std")]       public double PriorStd      { get; set; }
    [JsonPropertyName("training_weeks")]  public int    TrainingWeeks { get; set; }
    [JsonPropertyName("event_factor")]    public double EventFactor   { get; set; }
}

public class FilesInfo
{
    [JsonPropertyName("csv")]   public string Csv   { get; set; } = "";
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("log")]   public string Log   { get; set; } = "";
}

public class TemporalPatterns
{
    [JsonPropertyName("day_of_week")]    public List<DayOfWeekEntry> DayOfWeek    { get; set; } = [];
    [JsonPropertyName("hourly")]         public List<HourlyEntry>    Hourly       { get; set; } = [];
    [JsonPropertyName("peak_hour")]      public string               PeakHour     { get; set; } = "";
    [JsonPropertyName("inactivity_gap")] public InactivityGapStats   InactivityGap { get; set; } = new();
    [JsonPropertyName("weekly_summary")] public List<WeeklySummaryEntry> WeeklySummary { get; set; } = [];
    [JsonPropertyName("weekly_mean")]    public double WeeklyMean { get; set; }
    [JsonPropertyName("weekly_std")]     public double WeeklyStd  { get; set; }
    [JsonPropertyName("weekly_min")]     public int    WeeklyMin  { get; set; }
    [JsonPropertyName("weekly_max")]     public int    WeeklyMax  { get; set; }

    [JsonIgnore]
    public bool HasData => DayOfWeek.Count > 0 || Hourly.Count > 0 || WeeklySummary.Count > 0;
}

public class DayOfWeekEntry
{
    [JsonPropertyName("day")]        public string Day        { get; set; } = "";
    [JsonPropertyName("percentage")] public double Percentage { get; set; }
    [JsonPropertyName("count")]      public int    Count      { get; set; }
}

public class HourlyEntry
{
    [JsonPropertyName("hour")]  public string Hour  { get; set; } = "";
    [JsonPropertyName("count")] public int    Count { get; set; }
}

public class InactivityGapStats
{
    [JsonPropertyName("mean_hours")]            public double MeanHours          { get; set; }
    [JsonPropertyName("median_hours")]          public double MedianHours        { get; set; }
    [JsonPropertyName("max_hours")]             public double MaxHours           { get; set; }
    [JsonPropertyName("std_dev_hours")]         public double StdDevHours        { get; set; }
    [JsonPropertyName("unusually_long_count")]  public int    UnusuallyLongCount { get; set; }
}

public class WeeklySummaryEntry
{
    [JsonPropertyName("week")]  public string Week  { get; set; } = "";
    [JsonPropertyName("count")] public int    Count { get; set; }
}

public class BetIntervalForecast
{
    [JsonPropertyName("title")]            public string Title           { get; set; } = "";
    [JsonPropertyName("time_remaining")]   public string TimeRemaining   { get; set; } = "";
    [JsonPropertyName("tweets_in_window")] public int    TweetsInWindow  { get; set; }
    [JsonPropertyName("predicted_total")]  public int    PredictedTotal  { get; set; }
    [JsonPropertyName("ci_lower")]         public int    CiLower         { get; set; }
    [JsonPropertyName("ci_upper")]         public int    CiUpper         { get; set; }
    [JsonPropertyName("sigma")]            public double Sigma           { get; set; }
    [JsonPropertyName("intervals")]        public List<IntervalProbability> Intervals { get; set; } = [];
}

public class IntervalProbability
{
    [JsonPropertyName("label")]        public string Label       { get; set; } = "";
    [JsonPropertyName("probability")]  public double Probability { get; set; }
    [JsonPropertyName("is_predicted")] public bool   IsPredicted { get; set; }
}
