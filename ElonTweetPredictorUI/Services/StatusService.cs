using System.Text.Json;
using ElonTweetPredictorUI.Models;

namespace ElonTweetPredictorUI.Services;

public interface IStatusService
{
    Task<PredictorStatus?> GetStatusAsync();
}

public class StatusService : IStatusService
{
    private readonly string _filePath;

    public StatusService(IConfiguration configuration)
    {
        var cachePath = configuration["CachePath"]
            ?? Path.Combine(Path.GetTempPath(), "predictor-cache");
        _filePath = Path.Combine(cachePath, "status.json");
    }

    public async Task<PredictorStatus?> GetStatusAsync()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var json = await File.ReadAllTextAsync(_filePath);
                return JsonSerializer.Deserialize<PredictorStatus>(json);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (IOException) when (attempt < 2)
            {
                await Task.Delay(100);
            }
        }
        return null;
    }
}
