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
    [JsonPropertyName("model")]             public ModelInfo       Model            { get; set; } = new();
    [JsonPropertyName("files")]             public FilesInfo       Files            { get; set; } = new();
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
