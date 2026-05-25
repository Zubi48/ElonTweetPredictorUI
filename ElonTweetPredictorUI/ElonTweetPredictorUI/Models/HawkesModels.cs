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
    [JsonPropertyName("prediction_window")]
    public HawkesPredictionWindow PredictionWindow { get; set; } = new();

    [JsonPropertyName("hawkes_params")]
    public HawkesParams HawkesParams { get; set; } = new();

    [JsonPropertyName("per_hour_rates")]
    public List<HawkesPerHourRate> PerHourRates { get; set; } = [];

    [JsonPropertyName("components")]
    public HawkesComponents Components { get; set; } = new();

    [JsonPropertyName("weights")]
    public HawkesWeights Weights { get; set; } = new();

    [JsonPropertyName("validation")]
    public HawkesValidation Validation { get; set; } = new();

    [JsonPropertyName("final_prediction")]
    public HawkesFinalPrediction FinalPrediction { get; set; } = new();
}

public class HawkesPredictionWindow
{
    [JsonPropertyName("start")]
    public string Start { get; set; } = "";

    [JsonPropertyName("end")]
    public string End { get; set; } = "";

    [JsonPropertyName("hours")]
    public double Hours { get; set; }

    [JsonPropertyName("day_of_week")]
    public string DayOfWeek { get; set; } = "";
}

public class HawkesParams
{
    [JsonPropertyName("mu")]
    public double Mu { get; set; }

    [JsonPropertyName("alpha")]
    public double Alpha { get; set; }

    [JsonPropertyName("beta")]
    public double Beta { get; set; }

    [JsonPropertyName("tau0")]
    public double Tau0 { get; set; }

    [JsonPropertyName("fitted")]
    public bool Fitted { get; set; }
}

public class HawkesComponents
{
    [JsonPropertyName("day_hour_total")]
    public double DayHourTotal { get; set; }

    [JsonPropertyName("hawkes_short_term")]
    public double? HawkesShortTerm { get; set; }

    [JsonPropertyName("hawkes_remaining_hours_pred")]
    public double HawkesRemainingHoursPred { get; set; }

    [JsonPropertyName("hawkes_hybrid_total")]
    public double HawkesHybridTotal { get; set; }
}

public class HawkesWeights
{
    [JsonPropertyName("inverse_mse")]
    public List<double> InverseMse { get; set; } = [];

    [JsonPropertyName("akaike")]
    public List<double> Akaike { get; set; } = [];

    [JsonPropertyName("diversity")]
    public List<double> Diversity { get; set; } = [];

    [JsonPropertyName("entropy")]
    public List<double> Entropy { get; set; } = [];

    [JsonPropertyName("final_median")]
    public List<double> FinalMedian { get; set; } = [];
}

public class HawkesValidation
{
    [JsonPropertyName("cv_windows")]
    public int CvWindows { get; set; }

    [JsonPropertyName("dow_hod_mse")]
    public double DowHodMse { get; set; }

    [JsonPropertyName("hawkes_hybrid_mse")]
    public double HawkesHybridMse { get; set; }
}

public class HawkesFinalPrediction
{
    [JsonPropertyName("tweets")]
    public double Tweets { get; set; }

    [JsonPropertyName("ci_lower")]
    public double CiLower { get; set; }

    [JsonPropertyName("ci_upper")]
    public double CiUpper { get; set; }
}

public class HawkesPerHourRate
{
    [JsonPropertyName("hour")]
    public int Hour { get; set; }

    [JsonPropertyName("day")]
    public string Day { get; set; } = "";

    [JsonPropertyName("rate")]
    public double Rate { get; set; }

    [JsonPropertyName("observations")]
    public int Observations { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";
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
