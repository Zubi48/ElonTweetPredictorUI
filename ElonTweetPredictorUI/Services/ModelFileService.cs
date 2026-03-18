namespace ElonTweetPredictorUI.Services;

public interface IModelFileService
{
    Task<(bool Success, string Message)> ReplaceModelAsync(
        Stream uploadedFileStream,
        string originalFileName,
        CancellationToken cancellationToken = default);
}

public class ModelFileService : IModelFileService
{
    private readonly string _modelPath;
    private readonly string _reloadFlagPath;

    public ModelFileService(IConfiguration configuration)
    {
        var dataPath = configuration["DataPath"] ?? ".";
        _modelPath = Path.Combine(dataPath, "bayesian_model.pkl");
        _reloadFlagPath = Path.Combine(dataPath, "reload.flag");
    }

    public async Task<(bool Success, string Message)> ReplaceModelAsync(
        Stream uploadedFileStream,
        string originalFileName,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(originalFileName);
        if (!string.Equals(extension, ".pkl", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".plk", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Invalid file type. Please upload a .pkl file.");
        }

        try
        {
            var directory = Path.GetDirectoryName(_modelPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = _modelPath + ".upload.tmp";
            await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await uploadedFileStream.CopyToAsync(fileStream, cancellationToken);
            }

            File.Move(tempPath, _modelPath, overwrite: true);
            await File.WriteAllTextAsync(
                _reloadFlagPath,
                $"reload requested by ui at {DateTime.UtcNow:O}",
                cancellationToken);

            return (true, "Model replaced successfully. Reload signal sent.");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to replace model file: {ex.Message}");
        }
    }
}
