namespace ElonTweetPredictorUI.Services;

public interface IDataFileService
{
    Task<DataFileDownloadResult?> ResolveAsync(string fileType);
}

public sealed record DataFileDownloadResult(string FilePath, string ContentType, string DownloadFileName);

public class DataFileService : IDataFileService
{
    private readonly string _dataPath;
    private readonly IStatusService _statusService;

    public DataFileService(IConfiguration configuration, IStatusService statusService)
    {
        _statusService = statusService;
        _dataPath = configuration["DataPath"] ?? ".";
    }

    public async Task<DataFileDownloadResult?> ResolveAsync(string fileType)
    {
        var normalized = fileType.Trim().ToLowerInvariant();
        if (normalized is not ("csv" or "log" or "logsjson"))
        {
            return null;
        }

        if (normalized == "logsjson")
        {
            var logsPath = Path.Combine(_dataPath, "logs.json");
            if (!File.Exists(logsPath))
            {
                return null;
            }

            return new DataFileDownloadResult(logsPath, "application/json", "logs.json");
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
            "log" => "application/json",
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
        var logsPath = Path.Combine(_dataPath, "logs.json");
        return File.Exists(logsPath) ? logsPath : null;
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
