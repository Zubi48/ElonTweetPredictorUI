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
builder.Services.AddSingleton<ILogService, LogService>();
builder.Services.AddSingleton<IModelFileService, ModelFileService>();
builder.Services.AddSingleton<IDataChangeNotifier, DataChangeNotifier>();
builder.Services.AddSingleton<IDataFileService, DataFileService>();
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
            && !context.Request.Path.StartsWithSegments("/hubs"),
    branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));

app.UseAntiforgery();

app.MapStaticAssets();

app.MapHub<PredictionHub>("/hubs/predictions");
app.MapPredictionApi();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
