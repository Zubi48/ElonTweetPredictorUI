using System.Text.Json;
using ElonTweetPredictorUI.Models;

namespace ElonTweetPredictorUI.Services;

public interface IBetProbabilityService
{
    Task<BetProbabilityEntry?> GetLatestAsync();
}

public class BetProbabilityService : IBetProbabilityService
{
    private readonly string _filePath;

    public BetProbabilityService(IConfiguration configuration)
    {
        var cachePath = configuration["CachePath"]
            ?? Path.Combine(Path.GetTempPath(), "predictor-cache");
        _filePath = Path.Combine(cachePath, "logs.json");
    }

    public async Task<BetProbabilityEntry?> GetLatestAsync()
    {
        if (!File.Exists(_filePath))
            return null;

        try
        {
            var lines = await File.ReadAllLinesAsync(_filePath);

            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                if (!line.Contains("\"bet_probabilities\""))
                    continue;

                try
                {
                    var entry = JsonSerializer.Deserialize<BetProbabilityEntry>(line);
                    if (entry is not null && entry.Type == "bet_probabilities")
                        return entry;
                }
                catch (JsonException)
                {
                    // skip malformed
                }
            }
        }
        catch
        {
            // file locked or other IO issue
        }

        return null;
    }
}
