using ElonTweetPredictorUI.Api;
using ElonTweetPredictorUI.Components;
using ElonTweetPredictorUI.Hubs;
using ElonTweetPredictorUI.Services;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddSingleton<IContainerRestartService, ContainerRestartService>();
builder.Services.AddSingleton<IStatusService, StatusService>();
builder.Services.AddSingleton<IProbabilityHistoryService, ProbabilityHistoryService>();
builder.Services.AddSingleton<IModelFileService, ModelFileService>();
builder.Services.AddSingleton<IDataChangeNotifier, DataChangeNotifier>();
builder.Services.AddSingleton<IDataFileService, DataFileService>();
builder.Services.AddSingleton<ITradingLogService, TradingLogService>();
builder.Services.AddSingleton<ISleepService, SleepService>();
builder.Services.AddSingleton<ITweetHeatmapService, TweetHeatmapService>();
builder.Services.AddSingleton<ITradingChangeNotifier, TradingChangeNotifier>();
builder.Services.AddSingleton<ITradingV2LogService, TradingV2LogService>();
builder.Services.AddSingleton<ITradingV2ChangeNotifier, TradingV2ChangeNotifier>();
builder.Services.AddSingleton<IVolumeFileService, VolumeFileService>();
builder.Services.AddHostedService<LogConverterService>();
builder.Services.AddSignalR();
builder.Services.AddHostedService<SignalRBridgeService>();

builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseForwardedHeaders();

app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "Elon Tweet Predictor API");
});

app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api")
            && !context.Request.Path.StartsWithSegments("/hubs")
            && !context.Request.Path.StartsWithSegments("/downloads")
            && !context.Request.Path.StartsWithSegments("/data-image")
            && !context.Request.Path.StartsWithSegments("/file-manager"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));

app.UseAntiforgery();

app.MapStaticAssets();

app.MapHub<PredictionHub>("/hubs/predictions");
app.MapPredictionApi();

app.MapGet("/downloads/{fileType}", async (string fileType, IDataFileService dataFileService) =>
{
    var result = await dataFileService.ResolveAsync(fileType);
    if (result is null)
    {
        return Results.NotFound();
    }

    var stream = new FileStream(result.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    return Results.File(stream, result.ContentType, result.DownloadFileName);
});

app.MapGet("/file-manager/download/{filename}", (string filename, IVolumeFileService volumeFileService) =>
{
    var path = volumeFileService.ResolveDownloadPath(filename);
    if (path is null)
        return Results.NotFound();

    var ext = Path.GetExtension(filename).ToLowerInvariant();
    var contentType = ext switch
    {
        ".csv"   => "text/csv",
        ".log"   => "text/plain",
        ".txt"   => "text/plain",
        ".json"  => "application/json",
        ".jsonl" => "application/x-ndjson",
        ".png"   => "image/png",
        ".pkl"   => "application/octet-stream",
        _        => "application/octet-stream"
    };

    var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    return Results.File(stream, contentType, filename);
});

app.MapGet("/data-image/{filename}", (string filename, IConfiguration config) =>
{
    if (filename.Contains('/') || filename.Contains('\\') || filename.Contains(".."))
        return Results.BadRequest();

    var ext = Path.GetExtension(filename).ToLowerInvariant();
    if (ext is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".webp"))
        return Results.BadRequest();

    var dataPath = config["DataPath"] ?? ".";
    var filePath = Path.Combine(dataPath, filename);

    if (!File.Exists(filePath))
        return Results.NotFound();

    var contentType = ext switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif"            => "image/gif",
        ".webp"           => "image/webp",
        _                 => "image/png"
    };

    var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    return Results.File(stream, contentType);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
