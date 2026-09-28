using Playback.Api.Services.Ai.Agents;
using Playback.Api.Services.Ai.Providers;
using Playback.Api.Services.Audio;
using Playback.Api.Db;
using Playback.Api.Endpoints;
using Playback.Api.Middleware;
using Playback.Api.Services.Ai;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("PLAYBACK_VALIDATION_PORT") == "5081" ? "http://127.0.0.1:5081" :
    Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST") == "yes"
        ? "http://127.0.0.1:5079"
        : "http://127.0.0.1:5078");
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));
builder.Services.AddSingleton<PlaybackStore>();
builder.Services.AddSingleton<AiActivity>();
builder.Services.AddSingleton<GeminiLanguageModel>();
builder.Services.AddSingleton<JevTermClassifier>();
builder.Services.AddSingleton<SyntheticTermComparison>();
builder.Services.AddSingleton<ChatAgent>();
builder.Services.AddSingleton<NoteAgent>();
builder.Services.AddSingleton<TermReviewAgent>();
builder.Services.AddSingleton<TranslationAgent>();
builder.Services.AddSingleton<IAsrAdapter>(_ => AsrAdapters.Create());
builder.Services.AddSingleton<AsrProcessor>();
builder.Services.AddSingleton<SessionAudioRenderer>();
builder.Services.AddSingleton<AsrQueue>();
builder.Services.AddSingleton<WindowsAudioCaptureService>();

var app = builder.Build();
app.UseCors();
app.UseMiddleware<ApiExceptionMiddleware>();

app.MapHealth();
app.MapGet("/api/sessions/{id}/activity", async (string id, AiActivity activity) => await activity.Read(id));
app.MapCapture();
app.MapGroups();
app.MapSessions();
app.MapTesting();
app.MapNotes();
app.MapQuestions();
app.MapTerms();
app.MapChunks();

app.Services.GetRequiredService<AsrQueue>();
app.Services.GetRequiredService<NoteAgent>();
app.Services.GetRequiredService<TranslationAgent>();
app.Services.GetRequiredService<TermReviewAgent>();
app.Run();
