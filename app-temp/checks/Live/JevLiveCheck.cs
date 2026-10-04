using Playback.Api.Terms;

// Paid: ranks one synthetic term with Jev twice, expecting a ranking, usage and a cache hit.
// Needs PLAYBACK_JEV_LIVE_TEST=yes (set by run-jev-live.mjs) and JEV_API_KEY in the process environment.
static class JevLiveCheck
{
    public static async Task Run()
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_JEV_LIVE_TEST") != "yes")
            throw new InvalidOperationException("Set PLAYBACK_JEV_LIVE_TEST=yes for the synthetic Jev call");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JEV_API_KEY")))
            throw new InvalidOperationException("JEV_API_KEY is unavailable in the process environment");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var classifier = new JevTermClassifier();
        var first = await classifier.Rank("spectrogram", timeout.Token);
        var cached = await classifier.Rank("spectrogram", timeout.Token);
        Expect.That(first.JevProbability is >= 0 and <= 1 && first.JevConfidence is >= 0 and <= 1 &&
            first.JevRank.Length > 0 && first.Usage.ValueKind == System.Text.Json.JsonValueKind.Object && cached.Cached,
            "Jev live response did not contain ranking, confidence, usage, and a cache hit");
        Console.WriteLine($"Jev live passed: model={first.Model}, rank={first.JevRank}, probability={first.JevProbability}, confidence={first.JevConfidence}, cached={cached.Cached}");
    }
}
