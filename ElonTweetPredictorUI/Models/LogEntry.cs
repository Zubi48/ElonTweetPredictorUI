using System.Text.Json.Serialization;

namespace ElonTweetPredictorUI.Models;

public class LogEntry
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
    [JsonPropertyName("level")]     public string Level     { get; set; } = "";
    [JsonPropertyName("logger")]    public string Logger    { get; set; } = "";
    [JsonPropertyName("message")]   public string Message   { get; set; } = "";
}
