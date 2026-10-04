using Playback.Api.Db;
using Playback.Api.Terms;

// Key terms: which candidates the sources yield, which Jev rankings get highlighted, how a saved
// explanation is identified, and that the ranking cache saves paid calls.
static class TermChecks
{
    public static async Task Run()
    {
        var identified = TermCandidateExtractor.Find(
            [new Material { Id = "material-a", Text = "## Spectrogram\n**Fourier Transform** describes the signal." }],
            [
                new Transcript { Id = "speech-a", Original = "The Fourier Transform maps FFT bins.", RecognitionStatus = "recognized" },
                new Transcript { Id = "speech-b", Original = "<|NEUTRAL|>hello", RecognitionStatus = "recognized" },
                new Transcript { Id = "speech-c", Original = "FFT?", Uncertain = true }
            ]);
        Expect.That(identified.Any(x => x.Text == "Fourier Transform" && x.MaterialIds.Contains("material-a") && x.TranscriptIds.Contains("speech-a")),
            "Terms must retain transcript and material evidence");
        Expect.That(identified.Any(x => x.Text == "FFT" && x.TranscriptIds.Contains("speech-a")), "Recognized acronyms should be labeled");
        Expect.That(identified.All(x => x.Text != "NEUTRAL"), "SenseVoice markers must not become term labels");
        var contextual = TermCandidateExtractor.Find([], [new Transcript { Id = "mixed", Original = "我哋而家講緩存。cache stores data，用FFT同spectrogram分析。" }]);
        Expect.That(new[] { "緩存", "cache", "FFT", "spectrogram" }.All(term => contextual.Any(x => x.Text == term && x.Context.Contains("data"))),
            "Lowercase, two-character Chinese and acronyms next to Cantonese must reach contextual Jev ranking");

        var termRank = new JevRankResult("Spectrogram", true, "demo", 1, 0.92, "high", 0.87, default, false);
        Expect.That(JevTermClassifier.ShouldHighlight(termRank) &&
            !JevTermClassifier.ShouldHighlight(termRank with { JevProbability = 0.3 }) &&
            !JevTermClassifier.ShouldHighlight(termRank with { JevConfidence = 0.3 }) &&
            !JevTermClassifier.ShouldHighlight(termRank with { JevRank = "low" }),
            "Only Jev-selected terms should be highlighted");

        Expect.That(TermInsight.IdFor("session", " Spectrogram ") == TermInsight.IdFor("session", "spectrogram"),
            "Term reference IDs must be stable across case and whitespace");
        Expect.That(TermInsight.IdFor("session", "cache", "computer", "en") != TermInsight.IdFor("session", "cache", "toys", "en") &&
            TermInsight.IdFor("session", "cache", "computer", "en") != TermInsight.IdFor("session", "cache", "computer", "zh-Hant"),
            "Explanation identity must distinguish meaning and output language");

        await RankingCache();
        Console.WriteLine("Term checks passed: source-backed candidates, Cantonese context, highlight rule, explanation identity, Jev ranking cache");
    }

    static async Task RankingCache()
    {
        var previousKey = Environment.GetEnvironmentVariable("JEV_API_KEY");
        var previousOffline = Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST");
        try
        {
            // The handlers below answer in memory; clearing the offline switch lets the classifier reach them.
            Environment.SetEnvironmentVariable("JEV_API_KEY", "synthetic-fixture-key");
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", null);
            var handler = new TermRankHandler();
            var jev = new JevTermClassifier(handler);
            await Task.WhenAll(jev.Rank("Spectrogram", CancellationToken.None), jev.Rank("Spectrogram", CancellationToken.None));
            var cached = await jev.Rank("spectrogram", CancellationToken.None);
            var second = await jev.Rank("Phoneme", CancellationToken.None);
            Expect.That(handler.ModelRequests == 1 && handler.RankRequests == 2,
                "Jev should discover its model once and avoid duplicate paid term calls");
            Expect.That(cached.Cached && cached.JevRank == "high" && second.JevProbability == 0.92,
                "Cached Jev ranking should preserve the real model result");
            try
            {
                await new JevTermClassifier(new UnauthorizedJevHandler()).Rank("Synthetic", CancellationToken.None);
                throw new CheckFailed("Jev 401 was accepted");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("JEV_API_KEY", StringComparison.Ordinal)) { }
        }
        finally
        {
            Environment.SetEnvironmentVariable("JEV_API_KEY", previousKey);
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", previousOffline);
        }
    }
}
