namespace Playback.Infrastructure;

// Storage paths come from appsettings; process switches read the environment on each call.
public static class PlaybackEnvironment
{
    static string? dataPath;
    public static string? KeyPath { get; private set; }
    public static void ConfigureStorage(IConfiguration configuration)
    {
        dataPath = configuration["Storage:DataPath"];
        KeyPath = configuration["Storage:KeyPath"];
    }
    // PLAYBACK_OFFLINE_TEST=yes: listen on 5079, refuse every provider call, run no background scan.
    public static bool Offline => Read("PLAYBACK_OFFLINE_TEST") == "yes";

    // PLAYBACK_VALIDATION_PORT=5081: the isolated review API that validation scripts start.
    public static bool ValidationApi => Read("PLAYBACK_VALIDATION_PORT") == "5081";

    // Saved audio stays on disk and is not sent to the ASR provider until a restart without this switch.
    public static bool ExternalAsrPaused => Read("PLAYBACK_PAUSE_EXTERNAL_ASR") == "yes";
    public static bool AutomaticAsr => !Offline && !ExternalAsrPaused;
    public static bool ResumeSavedAsr => Read("PLAYBACK_RESUME_SAVED_ASR") != "no";

    // Finished chunks become Opus and their WAV is removed: on by default for dev and prod, off for local recordings.
    public static bool CompressAudio => Read("PLAYBACK_COMPRESS_AUDIO") switch
    {
        "yes" => true,
        "no" => false,
        _ => Stage is "dev" or "prod"
    };

    public static bool AutomaticNotes => Read("PLAYBACK_AUTO_NOTES") != "no";
    public static bool AutomaticTerms => Read("PLAYBACK_AUTO_TERMS") != "no";

    // The validation API works only on the one session it was started for.
    public static bool AllowsAutomaticWork(string sessionId) =>
        !ValidationApi || Read("PLAYBACK_VALIDATION_SESSION") == sessionId;

    // ENVIRONMENT=local: the Docker MongoDB on this machine (app-temp/compose.yaml), whatever PLAYBACK_MONGO_URI says.
    // dev or prod: PLAYBACK_MONGO_URI, required. Unset: PLAYBACK_MONGO_URI, else the local default (scripts and checks).
    public const string LocalMongoUri = "mongodb://127.0.0.1:27017";
    public static string? Stage => Read("ENVIRONMENT")?.ToLowerInvariant() switch
    {
        null or "" => null,
        var stage when stage is "local" or "dev" or "prod" => stage,
        _ => throw new InvalidOperationException("ENVIRONMENT must be local, dev or prod")
    };
    public static string MongoUri => Stage switch
    {
        "local" => LocalMongoUri,
        null => Read("PLAYBACK_MONGO_URI") ?? LocalMongoUri,
        var stage => Read("PLAYBACK_MONGO_URI") ?? throw new InvalidOperationException($"ENVIRONMENT={stage} needs PLAYBACK_MONGO_URI")
    };
    public static string Database => Read("PLAYBACK_MONGO_DATABASE") ?? "playback_prototype";

    // Synthetic test data may be written only by checks: offline, or the validation API on the test database.
    public static bool AllowsTestData => Offline || ValidationApi && Database == "playback_e2e";

    public static string ListenUrl => Read("PORT") is { } port
        ? int.TryParse(port, out var number) && number is > 0 and <= 65535
            ? $"http://0.0.0.0:{number}"
            : throw new InvalidOperationException("PORT must be between 1 and 65535")
        : ValidationApi ? "http://127.0.0.1:5081"
        : Offline ? "http://127.0.0.1:5079"
        : "http://127.0.0.1:5078";

    // app-temp/data, found from the API's build output (bin/<configuration>/<framework>/).
    public static string AudioFolder => DataFolder("audio");
    public static string CaptureFolder => DataFolder("local-capture");
    public static string PromptFolder => DataFolder("prompts");
    // The concept graph sidecar keeps one LightRAG workspace per session here (PLAYBACK_GRAPH_DIR for the sidecar).
    public static string GraphFolder => DataFolder("graph");

    static string DataFolder(string name) => !string.IsNullOrEmpty(dataPath)
        ? Path.GetFullPath(Path.Combine(dataPath, name))
        : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", name));

    static string? Read(string name) => Environment.GetEnvironmentVariable(name);
}
