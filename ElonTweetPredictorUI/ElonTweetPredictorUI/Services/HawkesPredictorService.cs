using System.Net.Http.Json;
using System.Text.Json;
using ElonTweetPredictorUI.Models;

namespace ElonTweetPredictorUI.Services;

public interface IHawkesPredictorService
{
    Task<HawkesHealthResponse?> GetHealthAsync(CancellationToken cancellationToken = default);
    Task<HawkesPredictResponse?> GetPredictionAsync(HawkesPredictRequest request, CancellationToken cancellationToken = default);
}

public sealed class HawkesPredictorService : IHawkesPredictorService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HawkesPredictorService> _logger;

    public HawkesPredictorService(IHttpClientFactory httpClientFactory, ILogger<HawkesPredictorService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<HawkesHealthResponse?> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("HawkesPredictor");
            return await client.GetFromJsonAsync<HawkesHealthResponse>("/health", JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reach Hawkes predictor /health endpoint.");
            return null;
        }
    }

    public async Task<HawkesPredictResponse?> GetPredictionAsync(HawkesPredictRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("HawkesPredictor");
            var response = await client.PostAsJsonAsync("/predict", request, JsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<HawkesPredictResponse>(JsonOptions, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reach Hawkes predictor /predict endpoint.");
            return null;
        }
    }
}
