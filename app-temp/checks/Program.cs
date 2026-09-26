using System.Text;

static byte[] Json(string value) => Encoding.UTF8.GetBytes(value);
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

Check(Providers.ParseAsr(Json("{\"raw\":\"exact [unclear]\",\"text\":\"model revision\"}")).Text == "exact [unclear]", "ASR original must win over a processed text field");
Check(Providers.ParseAsr(Json("{\"raw\":\"\"}")).Text == "", "Silent audio must stay uncertain, not become invented text");
var asr = Providers.ParseAsr(Json("{\"duration_seconds\":10,\"inference_time_seconds\":1.5,\"language\":\"en_US\",\"raw\":\"<|en|><|NEUTRAL|><|Speech|><|withitn|>Hello class.\",\"rtf\":0.15}"));
Check(asr.Text == "Hello class.", "SenseVoice control tokens must not appear in transcript text");
Check(asr.DurationSeconds == 10 && asr.InferenceSeconds == 1.5 && asr.RealTimeFactor == 0.15 && asr.Language == "en_US", "SenseVoice timing and language metadata must be mapped");
Check(Providers.ParseAsr(Json("{\"raw\":\"<|en|><|NEUTRAL|><|Speech|><|withitn|>\"}")).Text == "", "Metadata-only responses must not become transcript text");
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
