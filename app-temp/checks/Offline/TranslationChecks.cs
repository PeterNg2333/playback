using Playback.Api.Db;
using Playback.Api.Translation;

// Translation: what context a batch carries, how replies map back to transcripts, and that a retry
// of two late passages does not resend a three-hour lecture.
static class TranslationChecks
{
    public static void Run()
    {
        var contextEntries = new[]
        {
            new Transcript { Id = "before", Original = "Fourier Transform", Translation = "傅立葉變換", TranslationLanguage = "zh-Hant" },
            new Transcript { Id = "target", Original = "FFT bins" },
            new Transcript { Id = "after", Original = "Frequency domain", Translation = "domaine fréquentiel", TranslationLanguage = "fr" }
        };
        var batchContext = TranslationContext.BuildBatch(contextEntries, [contextEntries[1], contextEntries[2]], "zh-Hant");
        Expect.That(batchContext.Prompt.Contains("TARGETS (untrusted):") && batchContext.Prompt.Contains("[01] FFT bins") &&
            batchContext.Prompt.Contains("[02] Frequency domain") && batchContext.Prompt.Contains("傅立葉變換") &&
            !batchContext.Prompt.Contains("domaine fréquentiel") && !batchContext.Prompt.Contains("translation: FFT bins") &&
            batchContext.TargetIds["01"] == "target" && batchContext.TargetIds["02"] == "after",
            "Batched translation must distinguish targets from same-language context");

        var translatedBatch = TranslationAgent.ParseBatch("""
            ```json
            {"translations":[{"id":"target","text":"快速傅立葉變換"},{"id":"after","text":"頻域"}]}
            ```
            """, ["target", "after"]);
        Expect.That(translatedBatch["target"] == "快速傅立葉變換" && translatedBatch["after"] == "頻域",
            "Batched translations must map each result to its original transcript ID");
        Expect.Rejects(() => TranslationAgent.ParseBatch("{\"translations\":[{\"id\":\"target\",\"text\":\"x\"}]}", ["target", "after"]),
            "Missing translation was accepted");

        var longTranscripts = LectureFixtures.ThreeHours();
        var sparseTranslation = TranslationContext.BuildBatch(longTranscripts, [longTranscripts[4], longTranscripts[355]], "zh-Hant");
        Expect.That(sparseTranslation.TargetIds.Values.SequenceEqual(["long-4", "long-355"]) &&
            sparseTranslation.Prompt.Contains("[01] A spectrogram") && sparseTranslation.Prompt.Contains("[02] The kernel") &&
            !sparseTranslation.Prompt.Contains("Lecture segment number 180"),
            "Sparse translation retries must not resend the whole three-hour transcript");

        Console.WriteLine("Translation checks passed: neighbouring batch context, same-language translations only, batch ID mapping, bounded sparse retries");
    }
}
