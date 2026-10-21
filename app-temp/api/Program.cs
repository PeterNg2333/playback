using Playback.Api;
using Playback.Api.Activity;
using Playback.Api.Ask;
using Playback.Api.Audio;
using Playback.Api.Audio.Asr;
using Playback.Api.Audio.Recording;
using Playback.Api.Db;
using Playback.Api.Endpoints;
using Playback.Api.Notes;
using Playback.Api.Providers;
using Playback.Api.Terms;
using Playback.Api.Translation;
using Playback.Api.Security;
using Playback.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
PlaybackEnvironment.ConfigureStorage(builder.Configuration);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.WebHost.UseUrls(PlaybackEnvironment.ListenUrl);
builder.Services.AddHttpContextAccessor();
var access = new DemoAccess(builder.Environment);
access.AddServices(builder.Services);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));

var processingDefaults = builder.Configuration.GetSection("Processing").Get<ProcessingPolicy>() ?? ProcessingPolicy.Defaults;
if (Environment.GetEnvironmentVariable("PLAYBACK_AUTO_ORGANIZE") == "no") processingDefaults = processingDefaults with {OrganizationEnabled = false};
processingDefaults.Validate();
builder.Services.AddPlayback(processingDefaults);
if (Environment.GetEnvironmentVariable("PLAYBACK_AUTO_TRANSLATION") != "no")
{
    builder.Services.AddSingleton<TranslationAgent>();
    builder.Services.AddHostedService(s => s.GetRequiredService<TranslationAgent>());
}

var app = builder.Build();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseRouting();
if (!access.Enabled) app.UseCors();
access.Use(app);
if (access.Enabled) app.UseMiddleware<WorkspaceAccessMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapHealth();
app.MapSessions();
app.MapGroups();
app.MapCapture();
app.MapChunks();
app.MapRerun();
app.MapConcepts();
app.MapNotes();
app.MapProcessingSettings();
app.MapTerms();
app.MapAsk();
app.MapActivity();
app.MapTesting();
// Unknown API paths remain API errors instead of returning the SPA's HTML.
app.Map("/api/{**path}", () => Results.NotFound(new { error = "API route not found" }));
app.MapFallbackToFile("index.html");

app.Run();
