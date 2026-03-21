using System.Text.Json;
using ElonTweetPredictorUI.Models;

namespace ElonTweetPredictorUI.Services;

public interface ILogService
{
    Task<List<LogEntry>> GetRecentLogsAsync(int count = 100);
}

public class LogService : ILogService
{
    private readonly string _filePath;

    public LogService(IConfiguration configuration)
    {
        var dataPath = configuration["DataPath"] ?? ".";
        _filePath = Path.Combine(dataPath, "logs.json");
    }

    public async Task<List<LogEntry>> GetRecentLogsAsync(int count = 100)
    {
        if (!File.Exists(_filePath))
            return [];

        try
        {
            var lines = await File.ReadAllLinesAsync(_filePath);
            var result = new List<LogEntry>(Math.Min(count, lines.Length));

            for (var i = lines.Length - 1; i >= 0 && result.Count < count; i--)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                try
                {
                    var entry = JsonSerializer.Deserialize<LogEntry>(line);
                    if (entry is not null && !string.IsNullOrEmpty(entry.Message))
                        result.Add(entry);
                }
                catch (JsonException)
                {
                    // skip malformed lines
                }
            }

            result.Reverse(); // oldest → newest
            return result;
        }
        catch
        {
            return [];
        }
    }
}
