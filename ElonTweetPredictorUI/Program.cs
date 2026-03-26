using ElonTweetPredictorUI.Components;
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
builder.Services.AddSingleton<ILogService, LogService>();
builder.Services.AddSingleton<IModelFileService, ModelFileService>();
builder.Services.AddSingleton<IBetProbabilityService, BetProbabilityService>();
builder.Services.AddSingleton<IDataChangeNotifier, DataChangeNotifier>();
builder.Services.AddSingleton<IDataFileService, DataFileService>();
builder.Services.AddHostedService<LogConverterService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseForwardedHeaders();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/downloads/{fileType}", async (string fileType, IDataFileService dataFileService) =>
{
    var result = await dataFileService.ResolveAsync(fileType);
    if (result is null)
    {
        return Results.NotFound();
    }

    return Results.File(result.FilePath, result.ContentType, result.DownloadFileName, enableRangeProcessing: true);
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
