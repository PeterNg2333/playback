namespace Playback.Api;

// The switches and folders this API process takes from its environment. Every property reads the
// variable on each call, so a check can change a switch between two calls in one process.
public static class PlaybackEnvironment
{
    // PLAYBACK_OFFLINE_TEST=yes: listen on 5079, refuse every provider call, run no background scan.
    public static bool Offline => Read("PLAYBACK_OFFLINE_TEST") == "yes";

    // PLAYBACK_VALIDATION_PORT=5081: the isolated review API that validation scripts start.
    public static bool ValidationApi => Read("PLAYBACK_VALIDATION_PORT") == "5081";

    // Saved audio stays on disk and is not sent to the ASR provider until a restart without this switch.
    public static bool ExternalAsrPaused => Read("PLAYBACK_PAUSE_EXTERNAL_ASR") == "yes";
    public static bool AutomaticAsr => !Offline && !ExternalAsrPaused;
    public static bool ResumeSavedAsr => Read("PLAYBACK_RESUME_SAVED_ASR") != "no";

    public static bool AutomaticNotes => Read("PLAYBACK_AUTO_NOTES") != "no";
    public static bool AutomaticTerms => Read("PLAYBACK_AUTO_TERMS") != "no";
    public static bool AutomaticOrganization => Read("PLAYBACK_AUTO_ORGANIZE") == "yes";

    // The validation API works only on the one session it was started for.
    public static bool AllowsAutomaticWork(string sessionId) =>
        !ValidationApi || Read("PLAYBACK_VALIDATION_SESSION") == sessionId;

    public static string MongoUri => Read("PLAYBACK_MONGO_URI") ?? "mongodb://127.0.0.1:27017";
    public static string Database => Read("PLAYBACK_MONGO_DATABASE") ?? "playback_prototype";

    // Synthetic test data may be written only by checks: offline, or the validation API on the test database.
    public static bool AllowsTestData => Offline || ValidationApi && Database == "playback_e2e";

    public static string ListenUrl => ValidationApi ? "http://127.0.0.1:5081"
        : Offline ? "http://127.0.0.1:5079"
        : "http://127.0.0.1:5078";

    // app-temp/data, found from the API's build output (bin/<configuration>/<framework>/).
    public static string AudioFolder => DataFolder("audio");
    public static string CaptureFolder => DataFolder("local-capture");

    static string DataFolder(string name) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", name));

    static string? Read(string name) => Environment.GetEnvironmentVariable(name);
}
