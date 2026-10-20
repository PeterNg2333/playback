using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using NAudio.Wave;

// Real Windows loopback capture of the repository sample, through a fresh local API/database.
// The test retains its session, original audio and measurements. It never deletes data.
static class Wave1RecordingCheck
{
    public static async Task Run(string folder)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Real recording needs Windows audio");
        if (Environment.GetEnvironmentVariable("PLAYBACK_WAVE1_RECORDING_LIVE") != "yes")
            throw new InvalidOperationException("Wave 1 recording requires explicit live opt-in");
        var root = Path.GetFullPath("app-temp/data/validation/runs") + Path.DirectorySeparatorChar;
        folder = Path.GetFullPath(folder);
        if (!folder.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid evidence folder");
        Directory.CreateDirectory(folder);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) {
            BaseAddress = new Uri("http://127.0.0.1:5088/api/"), Timeout = TimeSpan.FromSeconds(100), MaxResponseContentBufferSize = 10_000_000 };
        using var health = JsonDocument.Parse(await http.GetStringAsync("health"));
        Expect.That(health.RootElement.GetProperty("database").GetString() == "playback_e2e" && health.RootElement.GetProperty("mongo").GetBoolean(),
            "Recording API is not the isolated test database");
        using var create = await http.PostAsJsonAsync("sessions", new { title = "Wave 1 live recording 2026-10-20" });
        create.EnsureSuccessStatusCode();
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString()!;
        using var language = await http.PutAsJsonAsync($"sessions/{id}/languages", new { asrLanguage = "en", noteLanguage = "zh-Hant" });
        language.EnsureSuccessStatusCode();
        await File.WriteAllTextAsync(Path.Combine(folder, "session-id.txt"), id);
        var observations = new List<object>();
        var watch = Stopwatch.StartNew();
        long? firstTranscript = null, firstNote = null;
        try
        {
            using var reader = new MediaFoundationReader("app-temp/data/test-audio/sampleAudio/sampleAudio.m4a");
            using var output = new WasapiPlayerBuilder().WithSharedMode().Build();
            output.Init(reader);
            using var start = await http.PostAsJsonAsync("capture/start", new { sessionId = id, sourceMode = "system" });
            start.EnsureSuccessStatusCode();
            using var state = JsonDocument.Parse(await http.GetStringAsync("capture/status"));
            Expect.That(state.RootElement.GetProperty("state").GetString() == "recording", "Actual loopback capture did not start");
            output.Play();
            Console.WriteLine($"Recording repository sample over real Windows loopback for three minutes; session {id}");
            while (watch.Elapsed < TimeSpan.FromMinutes(3))
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                using var session = JsonDocument.Parse(await http.GetStringAsync($"sessions/{id}"));
                var value = session.RootElement;
                var transcripts = value.GetProperty("transcripts").GetArrayLength();
                var version = value.GetProperty("noteVersion").GetInt32();
                if (transcripts > 0) firstTranscript ??= watch.ElapsedMilliseconds;
                if (version > 0) firstNote ??= watch.ElapsedMilliseconds;
                observations.Add(new { elapsedMs = watch.ElapsedMilliseconds, transcripts, version });
                Console.WriteLine($"Recording {watch.Elapsed.TotalSeconds:0}s: {transcripts} transcripts, note v{version}");
            }
            output.Stop();
        }
        finally
        {
            using var stop = await http.PostAsync("capture/stop", null);
            stop.EnsureSuccessStatusCode();
            await File.WriteAllTextAsync(Path.Combine(folder, "observations.json"), JsonSerializer.Serialize(new { id, firstTranscript, firstNote, observations }));
        }
        await Settle(http, id, folder, "");
        Console.WriteLine($"Recording measurements: first transcript {firstTranscript} ms, first note {firstNote} ms from capture start.");
    }

    public static async Task Review(string folder)
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_WAVE1_RECORDING_LIVE") != "yes") throw new InvalidOperationException("Live review needs opt-in");
        var id = (await File.ReadAllTextAsync(Path.Combine(folder, "session-id.txt"))).Trim();
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) {
            BaseAddress = new Uri("http://127.0.0.1:5088/api/"), Timeout = TimeSpan.FromSeconds(100), MaxResponseContentBufferSize = 10_000_000 };
        await Settle(http, id, folder, "recovered-");
    }

    static async Task Settle(HttpClient http, string id, string folder, string prefix)
    {
        // A note version alone is insufficient: every saved ASR chunk and note job must finish.
        var settled = false;
        for (var attempt = 0; attempt < 84; attempt++)
        {
            using var session = JsonDocument.Parse(await http.GetStringAsync($"sessions/{id}"));
            var value = session.RootElement;
            var chunks = value.GetProperty("chunks").EnumerateArray().ToList();
            var pending = chunks.Any(x => x.GetProperty("status").GetString() is "pending-asr" or "transcribing" or "asr-error" or "asr-manual");
            var notesPending = value.GetProperty("transcripts").EnumerateArray().Any(x =>
                !string.IsNullOrWhiteSpace(x.GetProperty("original").GetString()) && x.GetProperty("noteStatus").GetString() is "pending" or "processing" or "failed");
            using var activity = JsonDocument.Parse(await http.GetStringAsync($"sessions/{id}/activity?includePrompt=false"));
            var running = activity.RootElement.EnumerateArray().Any(x => x.GetProperty("status").GetString() is "running" or "queued");
            if (!pending && notesPending && !running && attempt == 2)
            {
                // One explicit recovery of this test session's uncited sources; no new audio is captured.
                using var repair = await http.PostAsJsonAsync($"sessions/{id}/notes/repair", new { basedOnVersion = value.GetProperty("noteVersion").GetInt32() });
                await File.WriteAllTextAsync(Path.Combine(folder, prefix + "repair.json"), await repair.Content.ReadAsStringAsync());
                repair.EnsureSuccessStatusCode();
            }
            Console.WriteLine($"Settling saved recording: ASR pending={pending}, notes pending={notesPending}, jobs running={running}");
            if (!pending && !notesPending && !running && value.GetProperty("noteVersion").GetInt32() > 0) { settled = true; break; }
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
        var snapshot = await http.GetStringAsync($"sessions/{id}");
        await File.WriteAllTextAsync(Path.Combine(folder, prefix + "session.json"), snapshot);
        await File.WriteAllTextAsync(Path.Combine(folder, prefix + "activity.json"), await http.GetStringAsync($"sessions/{id}/activity?includePrompt=false"));
        using var final = JsonDocument.Parse(snapshot);
        Expect.That(settled && final.RootElement.GetProperty("chunks").GetArrayLength() > 0 && final.RootElement.GetProperty("transcripts").GetArrayLength() > 0 &&
            final.RootElement.GetProperty("noteVersion").GetInt32() > 0, "Recording did not produce saved audio, real ASR and notes; retained evidence describes the failure");
        Console.WriteLine($"Saved recording fully settled; evidence retained at {folder}");
    }
}
