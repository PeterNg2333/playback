using Playback.Infrastructure;

// ENVIRONMENT picks the database: local is the Docker MongoDB whatever PLAYBACK_MONGO_URI says, dev and prod need the URI,
// unset keeps the URI or the local default, and any other value is refused. Only process variables change; nothing connects.
static class DatabaseTargetChecks
{
    public static void Run()
    {
        var saved = (Stage: Environment.GetEnvironmentVariable("ENVIRONMENT"), Uri: Environment.GetEnvironmentVariable("PLAYBACK_MONGO_URI"));
        const string cloud = "mongodb+srv://user:secret@cluster.example.net/";
        string Target(string? stage, string? uri)
        {
            Environment.SetEnvironmentVariable("ENVIRONMENT", stage);
            Environment.SetEnvironmentVariable("PLAYBACK_MONGO_URI", uri);
            return PlaybackEnvironment.MongoUri;
        }
        try
        {
            Expect.That(Target("local", cloud) == PlaybackEnvironment.LocalMongoUri && Target("LOCAL", null) == PlaybackEnvironment.LocalMongoUri,
                "ENVIRONMENT=local does not use the local Docker MongoDB");
            Expect.That(Target("dev", cloud) == cloud && Target("prod", cloud) == cloud && Target(null, cloud) == cloud,
                "dev, prod or an unset ENVIRONMENT does not use PLAYBACK_MONGO_URI");
            Expect.That(Target(null, null) == PlaybackEnvironment.LocalMongoUri, "An unset ENVIRONMENT without a URI lost the local default");
            Expect.Rejects(() => Target("prod", null), "ENVIRONMENT=prod without PLAYBACK_MONGO_URI fell back to another database");
            Expect.Rejects(() => Target("staging", cloud), "An unknown ENVIRONMENT was accepted");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENVIRONMENT", saved.Stage);
            Environment.SetEnvironmentVariable("PLAYBACK_MONGO_URI", saved.Uri);
        }
        Console.WriteLine("Database target checks passed: ENVIRONMENT=local uses the Docker MongoDB, dev/prod require PLAYBACK_MONGO_URI, " +
            "unset keeps the URI or the local default, unknown values refused (process variables only)");
    }
}
