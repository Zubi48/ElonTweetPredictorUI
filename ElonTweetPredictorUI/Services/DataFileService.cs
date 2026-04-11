namespace ElonTweetPredictorUI.Services;

public interface IDataFileService
{
    Task<DataFileDownloadResult?> ResolveAsync(string fileType);
}

public sealed record DataFileDownloadResult(string FilePath, string ContentType, string DownloadFileName);

public class DataFileService : IDataFileService
{
    private readonly string _dataPath;
    private readonly string _cachePath;
    private readonly IStatusService _statusService;

    public DataFileService(IConfiguration configuration, IStatusService statusService)
    {
        _statusService = statusService;
        _dataPath = configuration["DataPath"] ?? ".";
        _cachePath = configuration["CachePath"]
            ?? Path.Combine(Path.GetTempPath(), "predictor-cache");
    }

    public async Task<DataFileDownloadResult?> ResolveAsync(string fileType)
    {
        var normalized = fileType.Trim().ToLowerInvariant();
        if (normalized is not ("csv" or "log" or "predictorlog" or "logsjson" or "model" or "tradeslog" or "sleeplog"))
        {
            return null;
        }

        if (normalized == "predictorlog")
        {
            var logPath = Path.Combine(_dataPath, "tweet_predictor.log");
            if (!File.Exists(logPath))
            {
                return null;
            }

            return new DataFileDownloadResult(logPath, "text/plain", "tweet_predictor.log");
        }

        if (normalized == "model")
        {
            var modelPath = Path.Combine(_dataPath, "bayesian_model.pkl");
            if (!File.Exists(modelPath))
            {
                return null;
            }

            return new DataFileDownloadResult(modelPath, "application/octet-stream", "bayesian_model.pkl");
        }

        if (normalized == "tradeslog")
        {
            var tradesPath = Path.Combine(_dataPath, "trades.log");
            if (!File.Exists(tradesPath))
            {
                return null;
            }

            return new DataFileDownloadResult(tradesPath, "text/plain", "trades.log");
        }
        if (normalized == "logsjson")
        {
            var logsJsonPath = Path.Combine(_cachePath, "logs.json");
            if (!File.Exists(logsJsonPath))
            {
                return null;
            }

            return new DataFileDownloadResult(logsJsonPath, "application/json", "logs.json");
        }

        var status = await _statusService.GetStatusAsync();

        var statusPath = normalized switch
        {
            "csv" => status?.Files.Csv,
            "log" => status?.Files.Log,
            _ => null
        };

        var resolvedPath = ResolveExistingPath(statusPath, normalized);
        if (resolvedPath is null)
        {
            return null;
        }

        var downloadFileName = Path.GetFileName(resolvedPath);
        var contentType = normalized switch
        {
            "csv" => "text/csv",
            "log" => "text/plain",
            _ => "application/octet-stream"
        };

        return new DataFileDownloadResult(resolvedPath, contentType, downloadFileName);
    }

    private string? ResolveExistingPath(string? statusPath, string fileType)
    {
        if (!string.IsNullOrWhiteSpace(statusPath))
        {
            var candidate = statusPath;
            if (!Path.IsPathRooted(candidate))
            {
                candidate = Path.Combine(_dataPath, candidate);
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return fileType switch
        {
            "log" => ResolveFallbackLogPath(),
            "csv" => ResolveFallbackCsvPath(),
            _ => null
        };
    }

    private string? ResolveFallbackLogPath()
    {
        var logPath = Path.Combine(_dataPath, "tweet_predictor.log");
        return File.Exists(logPath) ? logPath : null;
    }

    private string? ResolveFallbackCsvPath()
    {
        if (!Directory.Exists(_dataPath))
        {
            return null;
        }

        return Directory.EnumerateFiles(_dataPath, "*.csv", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
