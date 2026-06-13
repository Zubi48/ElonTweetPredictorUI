namespace ElonTweetPredictorUI.Services;

public record VolumeFileInfo(string FileName, long SizeBytes, DateTime LastModifiedUtc, bool Exists = true);

public interface IVolumeFileService
{
    IReadOnlyList<VolumeFileInfo> ListFiles();
    (bool Success, string Message) DeleteFile(string fileName);
    string? ResolveDownloadPath(string fileName);
    Task<(bool Success, string Message)> ReplaceFileAsync(
        string fileName,
        Stream stream,
        string uploadedFileName,
        CancellationToken ct = default);
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

    private static readonly HashSet<string> KnownUploadableFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "elonmusk_tweet_history.csv",
        "elonmusk_tweet_history_improved.csv"
    };

    public VolumeFileService(IConfiguration configuration)
    {
        _dataPath = configuration["DataPath"] ?? ".";
    }

    public IReadOnlyList<VolumeFileInfo> ListFiles()
    {
        var existingFiles = Directory.Exists(_dataPath)
            ? Directory
                .EnumerateFiles(_dataPath, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(f => f is not null && !IsExcluded(f))
                .Select(f =>
                {
                    var info = new FileInfo(Path.Combine(_dataPath, f!));
                    return new VolumeFileInfo(f!, info.Length, info.LastWriteTimeUtc);
                })
                .ToList()
            : [];

        var existingNames = existingFiles.Select(f => f.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var stubs = KnownUploadableFiles
            .Where(f => !existingNames.Contains(f))
            .Select(f => new VolumeFileInfo(f, 0, DateTime.MinValue, Exists: false));

        return existingFiles
            .Concat(stubs)
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

    public async Task<(bool Success, string Message)> ReplaceFileAsync(
        string fileName,
        Stream stream,
        string uploadedFileName,
        CancellationToken ct = default)
    {
        if (!IsValidFileName(fileName) || IsExcluded(fileName))
            return (false, "File is not manageable.");

        var uploadExt = Path.GetExtension(uploadedFileName);
        var targetExt = Path.GetExtension(fileName);
        if (!string.Equals(uploadExt, targetExt, StringComparison.OrdinalIgnoreCase))
            return (false, $"Expected a {targetExt} file, got {uploadExt}.");

        var targetPath = Path.Combine(_dataPath, fileName);
        var tempPath   = targetPath + ".upload.tmp";
        try
        {
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                await stream.CopyToAsync(fs, ct);

            File.Move(tempPath, targetPath, overwrite: true);

            var flagPath = Path.Combine(_dataPath, "reload.flag");
            await File.WriteAllTextAsync(flagPath, $"file replaced by ui at {DateTime.UtcNow:O}", ct);

            return (true, $"'{fileName}' replaced successfully. Reload signal sent.");
        }
        catch (Exception ex)
        {
            if (File.Exists(tempPath))
                try { File.Delete(tempPath); } catch { }
            return (false, $"Failed to replace '{fileName}': {ex.Message}");
        }
    }
}
