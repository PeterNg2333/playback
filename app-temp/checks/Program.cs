using System.Text;
using NAudio.Wave;

static byte[] Json(string value) => Encoding.UTF8.GetBytes(value);
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

Check(Providers.ParseAsr(Json("{\"raw\":\"exact [unclear]\",\"text\":\"model revision\"}")).Text == "exact [unclear]", "ASR original must win over a processed text field");
Check(Providers.ParseAsr(Json("{\"raw\":\"\"}")).Text == "", "Silent audio must stay uncertain, not become invented text");
var asr = Providers.ParseAsr(Json("{\"duration_seconds\":10,\"inference_time_seconds\":1.5,\"language\":\"en_US\",\"raw\":\"<|en|><|NEUTRAL|><|Speech|><|withitn|>Hello class.\",\"rtf\":0.15}"));
Check(asr.Text == "Hello class.", "SenseVoice control tokens must not appear in transcript text");
Check(asr.DurationSeconds == 10 && asr.InferenceSeconds == 1.5 && asr.RealTimeFactor == 0.15 && asr.Language == "en_US", "SenseVoice timing and language metadata must be mapped");
Check(Providers.ParseAsr(Json("{\"raw\":\"<|en|><|NEUTRAL|><|Speech|><|withitn|>\"}")).Text == "", "Metadata-only responses must not become transcript text");
Check(PlaybackStore.NeedsReview("The term was [unclear].") && !PlaybackStore.NeedsReview("") && !PlaybackStore.NeedsReview("This is unclear but audible."),
    "Only explicit uncertainty markers should trigger human review; empty ASR must not masquerade as confidence");
var identified = TermFinder.Find(
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
var pendingNotes = NoteGenerator.Pending(backlog);
Check(pendingNotes.Count == 40 && pendingNotes[0].Id == "late-0" && pendingNotes.Any(x => x.Id == "late-2") && pendingNotes.All(x => x.Id != "late-1" && x.Id != "empty"),
    "Note jobs must include late and failed transcripts in bounded batches without using a time cursor");
var floatFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
var quiet = new byte[480 * 4];
var voice = new byte[480 * 4];
for (var i = 0; i < 480; i++) BitConverter.GetBytes(0.08f).CopyTo(voice, i * 4);
Check(!AudioActivity.HasSound(quiet, floatFormat), "Quiet microphone data must not reset the no-sound warning");
Check(AudioActivity.HasSound(voice, floatFormat), "Audible microphone data must reset the no-sound warning");
try { Providers.ParseAsr(Json("{\"status\":\"ok\"}")); throw new Exception("Unknown ASR schema was accepted"); }
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
    Check(AsrProcessor.IsDigitalSilence(silentWav), "Digital silence should skip remote ASR");
    wav[44] = 1;
    File.WriteAllBytes(toneWav, wav);
    Check(!AsrProcessor.IsDigitalSilence(toneWav), "Nonzero audio must not be skipped as silence");
}
finally
{
    File.Delete(silentWav);
    File.Delete(toneWav);
}

var grounded = Providers.ParseGrounding(Json("""
{"steps":[{"type":"model_output","content":[{"type":"text","text":"Acoustic features summarize a waveform.","annotations":[{"type":"url_citation","url":"https://example.org/source","title":"Example source","start_index":0,"end_index":17},{"type":"url_citation","url":"javascript:alert(1)","title":"Unsafe","start_index":0,"end_index":17}]}]}]}
"""));
Check(grounded.Answer == "Acoustic features summarize a waveform.", "Grounded answer text was lost");
Check(grounded.Evidence.Count == 1, "Unsafe or missing citation was accepted");
Check(grounded.Evidence[0].StartIndex == 0 && grounded.Evidence[0].EndIndex == 17, "Citation span changed");
try { Providers.ParseGrounding(Json("{\"steps\":[]}")); throw new Exception("Empty grounded response was accepted"); }
catch (InvalidOperationException) { }
Console.WriteLine("Protocol checks passed: ASR original, silence, provider failures, grounded citation mapping");
