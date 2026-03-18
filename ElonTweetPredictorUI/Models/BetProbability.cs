using System.Text.Json.Serialization;

namespace ElonTweetPredictorUI.Models;

public class BetProbabilityEntry
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
    [JsonPropertyName("type")]      public string Type      { get; set; } = "";
    [JsonPropertyName("trackings")] public List<BetProbabilityTracking> Trackings { get; set; } = [];
}

public class BetProbabilityTracking
{
    [JsonPropertyName("tracking_id")]      public string TrackingId       { get; set; } = "";
    [JsonPropertyName("title")]            public string Title            { get; set; } = "";
    [JsonPropertyName("target")]           public int?   Target           { get; set; }
    [JsonPropertyName("tweets_in_window")] public int    TweetsInWindow   { get; set; }
    [JsonPropertyName("window_days")]      public double WindowDays       { get; set; }
    [JsonPropertyName("elapsed_days")]     public double ElapsedDays      { get; set; }
    [JsonPropertyName("remaining_days")]   public double RemainingDays    { get; set; }
    [JsonPropertyName("window_prediction")]public int    WindowPrediction { get; set; }
    [JsonPropertyName("window_ci_lower")]  public int    WindowCiLower    { get; set; }
    [JsonPropertyName("window_ci_upper")]  public int    WindowCiUpper    { get; set; }

    // Kelly bet fields — populated by the Python predictor when available.
    // Nullable so the UI falls back to local KellyCalculator until the script provides them.
    [JsonPropertyName("our_prob_yes_pct")]    public double? OurProbYesPct    { get; set; }
    [JsonPropertyName("market_prob_yes_pct")] public double? MarketProbYesPct { get; set; }
    [JsonPropertyName("edge_pct")]            public double? EdgePct          { get; set; }
    [JsonPropertyName("frac_kelly_pct")]      public double? FracKellyPct     { get; set; }
    [JsonPropertyName("bet_dollars")]         public double? BetDollars       { get; set; }
    [JsonPropertyName("side")]                public string? Side             { get; set; }
    [JsonPropertyName("tier")]                public string? Tier             { get; set; }
    [JsonPropertyName("tier_emoji")]          public string? TierEmoji        { get; set; }

    [JsonIgnore]
    public bool HasKellyData => Side is not null;
}
