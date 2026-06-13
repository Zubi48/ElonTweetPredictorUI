namespace ElonTweetPredictorUI.Services;

public record VolumeFileInfo(string FileName, long SizeBytes, DateTime LastModifiedUtc);

public interface IVolumeFileService
{
    IReadOnlyList<VolumeFileInfo> ListFiles();
    (bool Success, string Message) DeleteFile(string fileName);
    string? ResolveDownloadPath(string fileName);
}

public class VolumeFileService : IVolumeFileService
{
    private readonly string _dataPath;

    private static readonly HashSet<string> ExcludedExact = new(StringComparer.OrdinalIgnoreCase)
    {
        "simple-trades.log",
        "memory.json"
    };

    private static readonly string[] ExcludedPrefixes =
    [
        "scoreboard-",
        "quant-",
        "simple-strategy-"
    ];

    public VolumeFileService(IConfiguration configuration)
    {
        _dataPath = configuration["DataPath"] ?? ".";
    }

    public IReadOnlyList<VolumeFileInfo> ListFiles()
    {
        if (!Directory.Exists(_dataPath))
            return [];

        return Directory
            .EnumerateFiles(_dataPath, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(f => f is not null && !IsExcluded(f))
            .Select(f =>
            {
                var info = new FileInfo(Path.Combine(_dataPath, f!));
                return new VolumeFileInfo(f!, info.Length, info.LastWriteTimeUtc);
            })
            .OrderBy(f => f.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public (bool Success, string Message) DeleteFile(string fileName)
    {
        if (!IsValidFileName(fileName))
            return (false, "Invalid file name.");

        if (IsExcluded(fileName))
            return (false, "This file is not managed by the file manager.");

        var path = Path.Combine(_dataPath, fileName);
        if (!File.Exists(path))
            return (false, $"'{fileName}' does not exist.");

        try
        {
            File.Delete(path);
            return (true, $"'{fileName}' deleted.");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to delete '{fileName}': {ex.Message}");
        }
    }

    public string? ResolveDownloadPath(string fileName)
    {
        if (!IsValidFileName(fileName) || IsExcluded(fileName))
            return null;

        var path = Path.Combine(_dataPath, fileName);
        return File.Exists(path) ? path : null;
    }

    private static bool IsValidFileName(string fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && !fileName.Contains('/')
        && !fileName.Contains('\\')
        && !fileName.Contains("..");

    private static bool IsExcluded(string fileName) =>
        ExcludedExact.Contains(fileName)
        || ExcludedPrefixes.Any(p => fileName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
}
