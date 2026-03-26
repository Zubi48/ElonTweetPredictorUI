using System.Net.Sockets;

namespace ElonTweetPredictorUI.Services;

public interface IContainerRestartService
{
    Task<(bool Success, string Message)> RestartPredictorAsync(CancellationToken cancellationToken = default);
}

public class ContainerRestartService : IContainerRestartService
{
    private readonly string _containerName;
    private readonly string _socketPath;
    private readonly ILogger<ContainerRestartService> _logger;

    public ContainerRestartService(IConfiguration configuration, ILogger<ContainerRestartService> logger)
    {
        _containerName = configuration["PredictorContainerName"] ?? "elon-tweet-predictor";
        _socketPath = configuration["DockerSocketPath"] ?? "/var/run/docker.sock";
        _logger = logger;
    }

    public async Task<(bool Success, string Message)> RestartPredictorAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_socketPath))
            return (false, $"Docker socket not found at '{_socketPath}'. Ensure the socket is mounted in docker-compose.");

        try
        {
            var handler = new SocketsHttpHandler
            {
                ConnectCallback = async (_, token) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath), token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
            };

            using var client = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
            using var response = await client.PostAsync(
                $"/containers/{Uri.EscapeDataString(_containerName)}/restart",
                content: null,
                cancellationToken);

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.NoContent =>
                    (true, $"Container '{_containerName}' is restarting."),
                System.Net.HttpStatusCode.NotFound =>
                    (false, $"Container '{_containerName}' not found. Check the PredictorContainerName setting."),
                _ => (false, $"Docker API returned unexpected status {(int)response.StatusCode}.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restart container '{ContainerName}'.", _containerName);
            return (false, $"Failed to restart container: {ex.Message}");
        }
    }
}
