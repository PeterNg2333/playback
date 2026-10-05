using NAudio.Wave;
using Playback.Api;
using Playback.Api.Audio.Vad;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

// Prepares the offline fixtures under app-temp/data/validation: a 7-second sample clip, script-converted ASR
// display for saved runs, and VAD ranges. No network.
static class ValidationFixtures
{
    public static void Run()
    {
        var root = Path.GetFullPath("app-temp/data/validation");
        var fixtures = Path.Combine(root, "fixtures");
        Directory.CreateDirectory(fixtures);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var original = Path.GetFullPath("app-temp/data/test-audio/sampleAudio/sampleAudio.m4a");
        using var reader = new MediaFoundationReader(original);
        using var resampler = new MediaFoundationResampler(reader, new WaveFormat(16000, 16, 1));
        var audio = new byte[7 * 32000];
        var read = 0;
        while (read < audio.Length) { var count = resampler.Read(audio.AsSpan(read)); if (count == 0) break; read += count; }
        var clip = Path.Combine(fixtures, "sample-0-7.wav");
        using (var writer = new WaveFileWriter(clip, new WaveFormat(16000, 16, 1))) writer.Write(audio, 0, read);
        File.WriteAllText(Path.Combine(fixtures, "sample-0-7.json"), JsonSerializer.Serialize(new {
            id = "sample-0-7", original, originalSha256 = Hash(original), fromMs = 0, throughMs = 7000,
            audioSha256 = Hash(clip),
            reference = "This year I have tried to using AI to assist me to do the training, so other than the normal posting,",
            referenceKind = "unverified first-line hypothesis; live results did not establish alignment; exclude from accuracy scoring"
        }, new JsonSerializerOptions { WriteIndented = true }));
        var run = Path.Combine(root, "runs/2026-09-28-repair");
        foreach (var file in Directory.GetFiles(run, "asr-*.json"))
        {
            var json = JsonNode.Parse(File.ReadAllText(file))!;
            if (json["response"]?["text"]?.GetValue<string>() is not { } raw) continue;
            var language = json["config"]?["case"]?.GetValue<string>() == "english" ? "en" : "yue-en";
            json["appDisplay"] = LanguageSettings.CantoneseDisplay(raw, language, null);
            json["displayVersion"] = "Windows LCMapStringEx zh-Hant; script only";
            File.WriteAllText(file, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        var vad = new List<object>();
        foreach (var file in Directory.GetFiles(fixtures, "*.wav"))
        {
            var speech = VadAudio.Prepare(file, CancellationToken.None);
            vad.Add(new { file = Path.GetFileName(file), speech.HasSpeech, speech.FromMs, speech.ThroughMs, uploadBytes = speech.Wav.Length });
        }
        File.WriteAllText(Path.Combine(run, "vad-fixtures.json"), JsonSerializer.Serialize(vad, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Offline fixtures, VAD ranges and original/display comparisons saved; no network.");
    }
    static string Hash(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
}
