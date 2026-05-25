using System.Text.Json.Serialization;

namespace ElonTweetPredictorUI.Models;

public class HawkesPredictRequest
{
    [JsonPropertyName("window_start")]
    public string WindowStart { get; set; } = "";

    [JsonPropertyName("window_end")]
    public string WindowEnd { get; set; } = "";

    [JsonPropertyName("momentum_hours")]
    public int MomentumHours { get; set; } = 3;

    [JsonPropertyName("hawkes_horizon")]
    public int HawkesHorizon { get; set; } = 2;
}

public class HawkesPredictResponse
{
    [JsonPropertyName("window_start")]
    public string WindowStart { get; set; } = "";

    [JsonPropertyName("window_end")]
    public string WindowEnd { get; set; } = "";

    [JsonPropertyName("momentum_hours")]
    public int MomentumHours { get; set; }

    [JsonPropertyName("hawkes_horizon")]
    public int HawkesHorizon { get; set; }

    [JsonPropertyName("probability")]
    public double Probability { get; set; }

    [JsonPropertyName("hawkes_rate")]
    public double? HawkesRate { get; set; }

    [JsonPropertyName("momentum_score")]
    public double? MomentumScore { get; set; }

    [JsonPropertyName("recent_tweets")]
    public int? RecentTweets { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class HawkesHealthResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("hawkes_fitted")]
    public bool HawkesFitted { get; set; }

    [JsonPropertyName("data_rows")]
    public int DataRows { get; set; }
}
