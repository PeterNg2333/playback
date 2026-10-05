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

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.WebHost.UseUrls(PlaybackEnvironment.ListenUrl);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddSingleton<PlaybackStore>();
builder.Services.AddSingleton<AiActivity>();
builder.Services.AddSingleton<AiFlow>();

builder.Services.AddSingleton<GeminiLanguageModel>();
builder.Services.AddSingleton<JevTransport>();

builder.Services.AddSingleton<IAsrAdapter>(_ => AsrAdapters.Create());
builder.Services.AddSingleton<AsrProcessor>();
builder.Services.AddSingleton<AsrQueue>();
builder.Services.AddSingleton<WindowsAudioCaptureService>();
builder.Services.AddSingleton<SessionAudioRenderer>();

builder.Services.AddSingleton<TranslationAgent>();
builder.Services.AddSingleton<JevNoteGate>();
builder.Services.AddSingleton<NoteAgent>();
builder.Services.AddSingleton<JevTermClassifier>(s => new JevTermClassifier(s.GetRequiredService<JevTransport>()));
builder.Services.AddSingleton<TermReviewAgent>();
builder.Services.AddSingleton<SyntheticTermComparison>();
builder.Services.AddSingleton<ChatAgent>();

var app = builder.Build();
app.UseCors();
app.UseMiddleware<ApiExceptionMiddleware>();

app.MapHealth();
app.MapSessions();
app.MapGroups();
app.MapCapture();
app.MapChunks();
app.MapNotes();
app.MapTerms();
app.MapAsk();
app.MapActivity();
app.MapTesting();

// Background workers start their scan loops in their constructors; resolve them before serving.
app.Services.GetRequiredService<AsrQueue>();
app.Services.GetRequiredService<NoteAgent>();
app.Services.GetRequiredService<TranslationAgent>();
app.Services.GetRequiredService<TermReviewAgent>();
app.Run();
