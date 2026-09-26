using Playback.Api.Services.Ai.Agents;
using Playback.Api.Services.Ai.Providers;
using Playback.Api.Terms;
using Playback.Api.Services.Audio;
using Playback.Api.Db;

using System.Text;
using NAudio.Wave;

if (args is ["--gemini-live"])
{
    try { await GeminiLiveCheck.Run(); }
    catch (InvalidOperationException ex)
    {
        Console.Error.WriteLine($"Gemini live demo failed: {ex.Message}");
        Environment.ExitCode = 1;
    }
    catch (Exception ex)
    {
        var message = ex.Message;
        var key = Environment.GetEnvironmentVariable("GOOGLE_AI_STUDIO_API_KEY");
        if (!string.IsNullOrEmpty(key)) message = message.Replace(key, "[REDACTED]", StringComparison.Ordinal);
        Console.Error.WriteLine($"Gemini live demo failed ({ex.GetType().Name}): {message}");
        Environment.ExitCode = 1;
    }
    return;
}

static byte[] Json(string value) => Encoding.UTF8.GetBytes(value);
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

Check(SenseVoiceClient.ParseResponse(Json("{\"raw\":\"exact [unclear]\",\"text\":\"model revision\"}")).Text == "exact [unclear]", "ASR original must win over a processed text field");
Check(SenseVoiceClient.ParseResponse(Json("{\"raw\":\"\"}")).Text == "", "Silent audio must stay uncertain, not become invented text");
var asr = SenseVoiceClient.ParseResponse(Json("{\"duration_seconds\":10,\"inference_time_seconds\":1.5,\"language\":\"en_US\",\"raw\":\"<|en|><|NEUTRAL|><|Speech|><|withitn|>Hello class.\",\"rtf\":0.15}"));
Check(asr.Text == "Hello class.", "SenseVoice control tokens must not appear in transcript text");
Check(asr.DurationSeconds == 10 && asr.InferenceSeconds == 1.5 && asr.RealTimeFactor == 0.15 && asr.Language == "en_US", "SenseVoice timing and language metadata must be mapped");
Check(SenseVoiceClient.ParseResponse(Json("{\"raw\":\"<|en|><|NEUTRAL|><|Speech|><|withitn|>\"}")).Text == "", "Metadata-only responses must not become transcript text");
Check(PlaybackStore.NeedsReview("The term was [unclear].") && !PlaybackStore.NeedsReview("") && !PlaybackStore.NeedsReview("This is unclear but audible."),
    "Only explicit uncertainty markers should trigger human review; empty ASR must not masquerade as confidence");
var identified = TermCandidateExtractor.Find(
    [new Material { Id = "material-a", Text = "## Spectrogram\n**Fourier Transform** describes the signal." }],
    [new Transcript { Id = "speech-a", Original = "The Fourier Transform maps FFT bins.", RecognitionStatus = "recognized" },
     new Transcript { Id = "speech-b", Original = "<|NEUTRAL|>hello", RecognitionStatus = "recognized" },
     new Transcript { Id = "speech-c", Original = "FFT?", Uncertain = true }]);
Check(identified.Any(x => x.Text == "Fourier Transform" && x.MaterialIds.Contains("material-a") && x.TranscriptIds.Contains("speech-a")), "Terms must retain transcript and material evidence");
Check(identified.Any(x => x.Text == "FFT" && x.TranscriptIds.Contains("speech-a")), "Recognized acronyms should be labeled");
Check(identified.All(x => x.Text != "NEUTRAL"), "SenseVoice markers must not become term labels");
var contextEntries = new[] {
    new Transcript { Id = "before", Original = "Fourier Transform", Translation = "傅立葉變換", TranslationLanguage = "zh-Hant" },
    new Transcript { Id = "target", Original = "FFT bins" },
    new Transcript { Id = "after", Original = "Frequency domain", Translation = "domaine fréquentiel", TranslationLanguage = "fr" }
};
var context = TranslationContext.Build(contextEntries, contextEntries[1], "zh-Hant");
Check(context.Contains("Fourier Transform") && context.Contains("傅立葉變換") && context.Contains("Frequency domain") &&
      !context.Contains("domaine fréquentiel") && context.Contains("TARGET [target]") && contextEntries[1].Original == "FFT bins",
    "Translation must use adjacent originals and matching-language translations without changing target original");
var backlog = Enumerable.Range(0, 45).Select(i => new Transcript { Id = $"late-{i}", StartMs = i * 1000, Original = "source text", NoteStatus = i == 1 ? "completed" : i == 2 ? "failed" : "pending" }).ToList();
backlog.Add(new Transcript { Id = "empty", StartMs = 1, Original = "", NoteStatus = "pending" });
var pendingNotes = NoteAgent.Pending(backlog);
Check(pendingNotes.Count == 40 && pendingNotes[0].Id == "late-0" && pendingNotes.Any(x => x.Id == "late-2") && pendingNotes.All(x => x.Id != "late-1" && x.Id != "empty"),
    "Note jobs must include late and failed transcripts in bounded batches without using a time cursor");
var floatFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
var quiet = new byte[480 * 4];
var voice = new byte[480 * 4];
for (var i = 0; i < 480; i++) BitConverter.GetBytes(0.08f).CopyTo(voice, i * 4);
Check(!AudioActivity.HasSound(quiet, floatFormat), "Quiet microphone data must not reset the no-sound warning");
Check(AudioActivity.HasSound(voice, floatFormat), "Audible microphone data must reset the no-sound warning");
try { SenseVoiceClient.ParseResponse(Json("{\"status\":\"ok\"}")); throw new Exception("Unknown ASR schema was accepted"); }
catch (InvalidOperationException) { }

var silentWav = Path.Combine(Path.GetTempPath(), $"playback-silent-{Guid.NewGuid():N}.wav");
var toneWav = Path.Combine(Path.GetTempPath(), $"playback-tone-{Guid.NewGuid():N}.wav");
try
{
    var wav = new byte[44 + 32000];
    Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
    BitConverter.GetBytes(wav.Length - 8).CopyTo(wav, 4);
    Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(wav, 8);
    BitConverter.GetBytes(16).CopyTo(wav, 16);
    BitConverter.GetBytes((short)1).CopyTo(wav, 20);
    BitConverter.GetBytes((short)1).CopyTo(wav, 22);
    BitConverter.GetBytes(16000).CopyTo(wav, 24);
    BitConverter.GetBytes(32000).CopyTo(wav, 28);
    BitConverter.GetBytes((short)2).CopyTo(wav, 32);
    BitConverter.GetBytes((short)16).CopyTo(wav, 34);
    Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
    BitConverter.GetBytes(32000).CopyTo(wav, 40);
    File.WriteAllBytes(silentWav, wav);
    Check(AudioSilence.IsDigitalSilence(silentWav), "Digital silence should skip remote ASR");
    wav[44] = 1;
    File.WriteAllBytes(toneWav, wav);
    Check(!AudioSilence.IsDigitalSilence(toneWav), "Nonzero audio must not be skipped as silence");
}
finally
{
    File.Delete(silentWav);
    File.Delete(toneWav);
}

var grounded = GeminiLanguageModel.ParseGrounding(Json("""
{"candidates":[{"content":{"parts":[{"text":"Acoustic features summarize a waveform."}]},"groundingMetadata":{"groundingChunks":[{"web":{"uri":"https://example.org/source","title":"Example source"}},{"web":{"uri":"javascript:alert(1)","title":"Unsafe"}}],"searchEntryPoint":{"renderedContent":"<div>Search suggestions</div>"}}}]}
"""));
Check(grounded.Answer == "Acoustic features summarize a waveform.", "Grounded answer text was lost");
Check(grounded.Evidence.Count == 1, "Unsafe or missing citation was accepted");
Check(grounded.Evidence[0].Url == "https://example.org/source" && grounded.SearchSuggestions.Contains("Search suggestions"),
    "Vertex citation or search suggestions were lost");
try { GeminiLanguageModel.ParseGrounding(Json("{\"candidates\":[]}")); throw new Exception("Empty grounded response was accepted"); }
catch (InvalidOperationException) { }
var previousJevKey = Environment.GetEnvironmentVariable("JEV_API_KEY");
var previousOffline = Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST");
try
{
    Environment.SetEnvironmentVariable("JEV_API_KEY", "synthetic-fixture-key");
    Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", null);
    var jevHandler = new DemoJevHandler();
    var jev = new JevTermClassifier(jevHandler);
    var first = await Task.WhenAll(jev.Rank("Spectrogram", CancellationToken.None),
                                   jev.Rank("Spectrogram", CancellationToken.None));
    var cached = (JevRankResult)await jev.Rank("spectrogram", CancellationToken.None);
    var second = (JevRankResult)await jev.Rank("Phoneme", CancellationToken.None);
    Check(jevHandler.ModelRequests == 1 && jevHandler.RankRequests == 2,
        "Jev should discover its model once and avoid duplicate paid term calls");
    Check(cached.Cached && cached.JevRank == "high" && second.JevProbability == 0.92,
        "Cached Jev ranking should preserve the real model result");
}
finally
{
    Environment.SetEnvironmentVariable("JEV_API_KEY", previousJevKey);
    Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", previousOffline);
}
Console.WriteLine("Protocol checks passed: ASR original, silence, Vertex citations, Jev ranking cache");

sealed class DemoJevHandler : HttpMessageHandler
{
    public int ModelRequests;
    public int RankRequests;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath;
        if (request.Method == HttpMethod.Get && path == "/v1/models")
        {
            Interlocked.Increment(ref ModelRequests);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"models\":[{\"name\":\"demo-jev\"}]}")
            });
        }
        if (request.Method == HttpMethod.Post && path == "/v1/systemone")
        {
            Interlocked.Increment(ref RankRequests);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("{\"answers\":{\"explain\":{\"noul\":0.92},\"category\":{\"choice\":\"high\",\"confidence\":0.87}},\"usage\":{\"input_tokens\":5}}")
            });
        }
        throw new InvalidOperationException("Unexpected Jev fixture request");
    }
}
