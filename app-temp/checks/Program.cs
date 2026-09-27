using Playback.Api.Services.Ai.Agents;
using Playback.Api.Services.Ai.Providers;
using Playback.Api.Terms;
using Playback.Api.Services.Audio;
using Playback.Api.Db;
using Playback.Api.Endpoints;

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
if (args is ["--week3", var folder])
{
    Week3OfflineCheck.Run(folder);
    return;
}
if (args is ["--jev-live"])
{
    try
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_JEV_LIVE_TEST") != "yes")
            throw new InvalidOperationException("Set PLAYBACK_JEV_LIVE_TEST=yes for the synthetic Jev call");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JEV_API_KEY")))
            throw new InvalidOperationException("JEV_API_KEY is unavailable in the process environment");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var classifier = new JevTermClassifier();
        var first = (JevRankResult)await classifier.Rank("spectrogram", timeout.Token);
        var cached = (JevRankResult)await classifier.Rank("spectrogram", timeout.Token);
        Check(first.JevProbability is >= 0 and <= 1 && first.JevConfidence is >= 0 and <= 1 &&
              first.JevRank.Length > 0 && first.Usage.ValueKind == System.Text.Json.JsonValueKind.Object && cached.Cached,
            "Jev live response did not contain ranking, confidence, usage, and a cache hit");
        Console.WriteLine($"Jev live passed: model={first.Model}, rank={first.JevRank}, probability={first.JevProbability}, confidence={first.JevConfidence}, cached={cached.Cached}");
    }
    catch (Exception ex)
    {
        var message = ex.Message;
        var key = Environment.GetEnvironmentVariable("JEV_API_KEY");
        if (!string.IsNullOrEmpty(key)) message = message.Replace(key, "[REDACTED]", StringComparison.Ordinal);
        Console.Error.WriteLine($"Jev live failed ({ex.GetType().Name}): {message}");
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
var noteEdits = NoteChangeLog.Build("Alpha [old]\nKeep", "Alpha [old]\nInserted [new]\nKeep", ["old", "new"], []);
Check(noteEdits.Count == 1 && noteEdits[0].Kind == "insert" && noteEdits[0].Line == 2 &&
      noteEdits[0].Text == "Inserted [new]" && noteEdits[0].TranscriptIds.SequenceEqual(["new"]),
    "Note edit log must locate an inserted line and its cited transcript");
var removedEdits = NoteChangeLog.Build("Remove [old]\nKeep", "Keep", ["old"], []);
Check(removedEdits.Count == 1 && removedEdits[0].Kind == "remove" && removedEdits[0].TranscriptIds.SequenceEqual(["old"]),
    "Removed note lines must keep their former source citation");
var bulkEdits = NoteChangeLog.Build("", string.Join('\n', Enumerable.Repeat("Line [new]", 3_100)), ["new"], []);
Check(bulkEdits.Count == 1 && bulkEdits[0].Kind == "insert" && bulkEdits[0].TranscriptIds.SequenceEqual(["new"]),
    "Large notes must keep a bounded edit log with its cited source");
var termRank = new JevRankResult("Spectrogram", true, "demo", 1, 0.92, "high", 0.87, default, false);
Check(JevTermClassifier.ShouldHighlight(termRank) &&
      !JevTermClassifier.ShouldHighlight(termRank with { JevProbability = 0.3 }) &&
      !JevTermClassifier.ShouldHighlight(termRank with { JevConfidence = 0.3 }) &&
      !JevTermClassifier.ShouldHighlight(termRank with { JevRank = "low" }),
    "Only Jev-selected terms should be highlighted");
Check(PlaybackStore.TermInsightId("session", " Spectrogram ") == PlaybackStore.TermInsightId("session", "spectrogram"),
    "Term reference IDs must be stable across case and whitespace");
var allowedTerm = new TermInsight { Id = "a".PadRight(64, 'a') };
NoteAgent.ValidateReferences($"A term [ref:{allowedTerm.Id}]", [allowedTerm]);
try { NoteAgent.ValidateReferences("Invented [ref:missing]", [allowedTerm]); throw new Exception("Invented note reference was accepted"); }
catch (InvalidOperationException) { }
var contextEntries = new[] {
    new Transcript { Id = "before", Original = "Fourier Transform", Translation = "傅立葉變換", TranslationLanguage = "zh-Hant" },
    new Transcript { Id = "target", Original = "FFT bins" },
    new Transcript { Id = "after", Original = "Frequency domain", Translation = "domaine fréquentiel", TranslationLanguage = "fr" }
};
var context = TranslationContext.Build(contextEntries, contextEntries[1], "zh-Hant");
Check(context.Contains("Fourier Transform") && context.Contains("傅立葉變換") && context.Contains("Frequency domain") &&
      !context.Contains("domaine fréquentiel") && context.Contains("TARGET [target]") && contextEntries[1].Original == "FFT bins",
    "Translation must use adjacent originals and matching-language translations without changing target original");
var batchContext = TranslationContext.BuildBatch(contextEntries, [contextEntries[1], contextEntries[2]], "zh-Hant");
Check(batchContext.Contains("TARGETS (untrusted):") && batchContext.Contains("[target] FFT bins") &&
      batchContext.Contains("[after] Frequency domain") && batchContext.Contains("傅立葉變換") &&
      !batchContext.Contains("domaine fréquentiel") && !batchContext.Contains("translation: FFT bins"),
    "Batched translation must distinguish targets from same-language context");
var translatedBatch = TranslationAgent.ParseBatch("""
```json
{"translations":[{"id":"target","text":"快速傅立葉變換"},{"id":"after","text":"頻域"}]}
```
""", ["target", "after"]);
Check(translatedBatch["target"] == "快速傅立葉變換" && translatedBatch["after"] == "頻域",
    "Batched translations must map each result to its original transcript ID");
try { TranslationAgent.ParseBatch("{\"translations\":[{\"id\":\"target\",\"text\":\"x\"}]}", ["target", "after"]); throw new Exception("Missing translation was accepted"); }
catch (InvalidOperationException) { }
var backlog = Enumerable.Range(0, 45).Select(i => new Transcript { Id = $"late-{i}", StartMs = i * 1000, Original = "source text", NoteStatus = i == 1 ? "completed" : i == 2 ? "failed" : "pending" }).ToList();
backlog.Add(new Transcript { Id = "empty", StartMs = 1, Original = "", NoteStatus = "pending" });
var pendingNotes = NoteAgent.Pending(backlog);
Check(pendingNotes.Count == 40 && pendingNotes[0].Id == "late-0" && pendingNotes.Any(x => x.Id == "late-2") && pendingNotes.All(x => x.Id != "late-1" && x.Id != "empty"),
    "Note jobs must include late and failed transcripts in bounded batches without using a time cursor");
var noteMaterials = Enumerable.Range(0, 7).Select(i => new Material { Id = $"material-{i}", Text = "synthetic" }).ToList();
var priorNote = new Note { MaterialIds = noteMaterials.Take(5).Select(x => x.Id).ToList() };
Check(NoteAgent.MaterialsForPrompt(noteMaterials, null, false).Count == 5 &&
      NoteAgent.MaterialsForPrompt(noteMaterials, priorNote, false).Select(x => x.Id).SequenceEqual(["material-5", "material-6"]) &&
      NoteAgent.MaterialsForPrompt(noteMaterials, priorNote, true).Count == 5,
    "Automatic notes should send only new materials while explicit revision can re-read the sources");
var longTranscripts = Enumerable.Range(0, 360).Select(i => new Transcript
{
    Id = $"long-{i}", StartMs = i * 30_000, EndMs = (i + 1) * 30_000,
    Original = i == 4 ? "A spectrogram shows frequency over time." :
        i == 355 ? "The kernel handles a system call." : $"Lecture segment number {i}."
}).ToList();
var longSession = new SessionView("synthetic", "Three hours", null, DateTime.UtcNow,
    "", 0, 0, false, "zh-Hant", [], longTranscripts, [], [], [], null);
var sparseTranslation = TranslationContext.BuildBatch(longTranscripts, [longTranscripts[4], longTranscripts[355]], "zh-Hant");
Check(sparseTranslation.Contains("[long-4]") && sparseTranslation.Contains("[long-355]") &&
      !sparseTranslation.Contains("[long-180]"),
    "Sparse translation retries must not resend the whole three-hour transcript");
var earlyAnswer = ChatContextBuilder.Build(longSession, new QuestionInput("What does spectrogram show?"));
var lateAnswer = ChatContextBuilder.Build(longSession, new QuestionInput("Explain the kernel?"));
Check(earlyAnswer.RelevantTranscripts[0].Id == "long-4" &&
      lateAnswer.RelevantTranscripts[0].Id == "long-355" &&
      earlyAnswer.Prompt.Contains("[long-4, 120000-150000 ms]") &&
      lateAnswer.Prompt.Contains("[long-355, 10650000-10680000 ms]"),
    "Three-hour questions must retrieve early and late transcript sources with exact audio times");
var citedEarly = ChatAgent.CitedEvidence(earlyAnswer, "A spectrogram shows frequency over time [long-4].");
Check(citedEarly.Length == 1 && citedEarly[0].Id == "long-4" &&
      citedEarly[0].Kind == "lecture" && citedEarly[0].Label == "120000-150000 ms",
    "Q&A evidence must link an actual cited transcript to its audio time");
var uncertainEvidence = ChatAgent.CitedEvidence(earlyAnswer, "The speaker said [unclear] about frequency [long-4].");
Check(uncertainEvidence.Length == 1 && uncertainEvidence[0].Id == "long-4",
    "ASR uncertainty markers must remain distinct from source citations");
var focusedEarly = ChatContextBuilder.Build(longSession, new QuestionInput("What does it show?", TranscriptId: "long-4"));
try { ChatAgent.CitedEvidence(focusedEarly, "A spectrogram shows frequency over time."); throw new Exception("Uncited focused source was accepted"); }
catch (InvalidOperationException) { }
try { ChatAgent.CitedEvidence(earlyAnswer, "A spectrogram shows frequency over time [invented]."); throw new Exception("Invented source was accepted"); }
catch (InvalidOperationException) { }
try { ChatAgent.CitedEvidence(earlyAnswer, "A spectrogram shows frequency over time [long-4] [invented]."); throw new Exception("Invented extra source was accepted"); }
catch (InvalidOperationException) { }
var materialSession = longSession with { Materials = [new Material { Id = "material-one", Name = "Handout", Text = "FFT means fast Fourier transform." }], Transcripts = [] };
var materialContext = ChatContextBuilder.Build(materialSession, new QuestionInput("What does FFT mean?"));
var citedMaterial = ChatAgent.CitedEvidence(materialContext, "FFT means fast Fourier transform [material-one].");
Check(citedMaterial.Length == 1 && citedMaterial[0].Kind == "material" && citedMaterial[0].Label == "Handout",
    "Material-only Q&A must link the cited handout");
var manyMaterials = Enumerable.Range(0, 7).Select(i => new Material
{
    Id = $"handout-{i}", Name = $"Handout {i}",
    Text = i == 6 ? new string('x', 3_200) + " Convolution combines two signals." : "Unrelated practice questions."
}).ToList();
var materialSearchSession = materialSession with { Materials = manyMaterials };
var foundLateMaterial = ChatContextBuilder.Build(materialSearchSession, new QuestionInput("What does convolution combine?"));
Check(foundLateMaterial.Materials.Any(x => x.Id == "handout-6") &&
      foundLateMaterial.Prompt.Contains("Convolution combines two signals."),
    "Q&A must retrieve a relevant later handout and include the matching passage");
var focusedMaterial = ChatContextBuilder.Build(materialSearchSession,
    new QuestionInput("What does convolution combine?", MaterialId: "handout-1"));
Check(focusedMaterial.Materials[0].Id == "handout-1",
    "An explicitly selected handout must remain first in the Q&A source set");
var oversizedTranscript = materialSession with
{
    Materials = [],
    Transcripts = [new Transcript { Id = "long-source", StartMs = 10_000, EndMs = 40_000,
        Original = new string('x', 10_000) + " Plasma oscillations depend on electron density." }]
};
var longSourceContext = ChatContextBuilder.Build(oversizedTranscript, new QuestionInput("What affects plasma oscillations?", TranscriptId: "long-source"));
Check(longSourceContext.Prompt.Contains("Plasma oscillations depend on electron density.") &&
      longSourceContext.Prompt.Length < 5_000,
    "Long transcript questions must include the matching passage in a bounded prompt");
var chineseTimeline = Enumerable.Range(0, 360).Select(i => new Transcript
{
    Id = $"zh-{i}", StartMs = i * 30_000, EndMs = (i + 1) * 30_000,
    Original = i == 5 ? "傅立葉變換把訊號分解成頻率成分，FFT 是快速計算法。" : $"第{i}段的課堂練習。"
}).ToList();
var chineseSession = longSession with { Transcripts = chineseTimeline };
var chineseAnswer = ChatContextBuilder.Build(chineseSession, new QuestionInput("傅立葉變換是甚麼？"));
Check(chineseAnswer.RelevantTranscripts[0].Id == "zh-5" &&
      chineseAnswer.Prompt.Contains("[zh-5, 150000-180000 ms]"),
    "Chinese questions must retrieve an early matching source from a long lecture");
var mixedAnswer = ChatContextBuilder.Build(chineseSession, new QuestionInput("FFT是甚麼？"));
Check(mixedAnswer.RelevantTranscripts[0].Id == "zh-5",
    "Mixed English and Chinese questions must preserve the English term");
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
    try { await new JevTermClassifier(new UnauthorizedJevHandler()).Rank("Synthetic", CancellationToken.None); throw new Exception("Jev 401 was accepted"); }
    catch (InvalidOperationException ex) when (ex.Message.Contains("JEV_API_KEY", StringComparison.Ordinal)) { }
}
finally
{
    Environment.SetEnvironmentVariable("JEV_API_KEY", previousJevKey);
    Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", previousOffline);
}
Console.WriteLine("Protocol checks passed: ASR original, silence, Vertex citations, Jev ranking cache, batched translation, source-backed Q&A and bounded long-lecture lookup");

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

sealed class UnauthorizedJevHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized));
}
