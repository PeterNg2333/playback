using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

// Paid, end to end: uploads the Week 3 sample as 40 chunks to a running test API (playback_e2e, background
// notes and terms off), waits for real ASR, asks one cited question and writes notes.
// The refactor runner retains the session/audio/artifacts; the legacy runner cleans up its own test session.
static class SampleAudioLiveCheck
{
    public static async Task Run(string folder)
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_SAMPLE_AUDIO_LIVE") != "yes") throw new InvalidOperationException("Sample audio live acceptance requires explicit PLAYBACK_SAMPLE_AUDIO_LIVE=yes");
        var origin = Environment.GetEnvironmentVariable("PLAYBACK_SAMPLE_LIVE_ORIGIN") ?? "http://127.0.0.1:5078";
        if (origin is not ("http://127.0.0.1:5078" or "http://127.0.0.1:5082")) throw new InvalidOperationException("Sample live API origin is not allowlisted");
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(origin + "/api/"),
            Timeout = TimeSpan.FromMinutes(3)
        };
        using (var health = await ReadJson(http, "health"))
        {
            var status = health.RootElement;
            if (status.GetProperty("database").GetString() != "playback_e2e" ||
                !status.GetProperty("mongo").GetBoolean() ||
                !status.GetProperty("gemini").GetBoolean() ||
                !status.GetProperty("automaticAsr").GetBoolean() ||
                status.GetProperty("autoNotes").GetBoolean() ||
                status.GetProperty("autoTerms").GetBoolean())
                throw new InvalidOperationException("Live API must use playback_e2e with MongoDB/Gemini/ASR ready and background notes/terms disabled");
        }

        string? sessionId = null;
        try
        {
            using var create = await http.PostAsJsonAsync("sessions", new { title = $"E2E demo Sample audio {Guid.NewGuid():N}" });
            create.EnsureSuccessStatusCode();
            using (var created = JsonDocument.Parse(await create.Content.ReadAsStreamAsync()))
                sessionId = created.RootElement.GetProperty("id").GetString();
            if (sessionId is null || sessionId.Length != 32) throw new InvalidOperationException("Test session was not created");

            foreach (var (sequence, wav, _) in SampleAudio.Chunks(folder))
            {
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(sessionId), "sessionId");
                form.Add(new StringContent("system"), "sourceId");
                form.Add(new StringContent(sequence.ToString()), "sequence");
                form.Add(new StringContent((sequence * 30_000).ToString()), "startMs");
                form.Add(new StringContent(((sequence + 1) * 30_000).ToString()), "endMs");
                form.Add(new StringContent(Convert.ToHexString(SHA256.HashData(wav)).ToLowerInvariant()), "sha256");
                form.Add(new StringContent(DateTime.UtcNow.AddMinutes(-20).AddSeconds(sequence * 30).ToString("O")), "recordedAt");
                var content = new ByteArrayContent(wav);
                content.Headers.ContentType = new("audio/wav");
                form.Add(content, "file", "lecture.wav");
                using var uploaded = await http.PostAsync("chunks", form);
                if (uploaded.StatusCode != System.Net.HttpStatusCode.Accepted)
                    throw new InvalidOperationException($"Chunk {sequence} upload returned HTTP {(int)uploaded.StatusCode}");
            }
            Console.WriteLine("Sample audio live: 40 WAV chunks saved locally; waiting for configured ASR results.");

            var deadline = DateTime.UtcNow.AddMinutes(15);
            var lastReport = DateTime.MinValue;
            JsonDocument? snapshot = null;
            try
            {
                while (DateTime.UtcNow < deadline)
                {
                    snapshot?.Dispose();
                    snapshot = await ReadJson(http, $"sessions/{sessionId}");
                    var chunks = snapshot.RootElement.GetProperty("chunks").EnumerateArray().ToArray();
                    var done = chunks.Count(Finished);
                    if (done == 40) break;
                    if (DateTime.UtcNow - lastReport > TimeSpan.FromSeconds(30))
                    {
                        Console.WriteLine($"Sample audio live: {done}/40 ASR chunks finished.");
                        lastReport = DateTime.UtcNow;
                    }
                    await Task.Delay(5_000);
                }
                if (snapshot is null) throw new InvalidOperationException("No session result was returned");
                var saved = snapshot.RootElement.GetProperty("chunks").EnumerateArray().ToArray();
                if (saved.Length != 40 || saved.Any(chunk => !Finished(chunk)))
                    throw new TimeoutException("ASR did not finish all 40 saved chunks in 15 minutes");
                var recognized = saved.Count(chunk => chunk.GetProperty("status").GetString() == "transcribed");
                var manual = saved.Count(chunk => chunk.GetProperty("status").GetString() == "asr-manual");
                if (manual > 0) throw new InvalidOperationException("Live ASR has failed chunks; inspect the retained test session before spending on notes");
                Console.WriteLine($"Sample audio live ASR: {recognized} transcribed, {manual} require manual retry, {40 - recognized - manual} empty or silent.");
                var source = snapshot.RootElement.GetProperty("transcripts").EnumerateArray()
                    .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.GetProperty("original").GetString()));
                if (source.ValueKind == JsonValueKind.Undefined)
                    throw new InvalidOperationException("Configured ASR returned no usable transcript for AI verification");
                var sourceId = source.GetProperty("id").GetString();
                using var asked = await http.PostAsJsonAsync($"sessions/{sessionId}/ask", new
                {
                    question = "Summarize the selected lecture passage in one sentence and cite its source.",
                    transcriptId = sourceId,
                    useWeb = false
                });
                if (!asked.IsSuccessStatusCode)
                    throw new InvalidOperationException($"Ask Playback returned HTTP {(int)asked.StatusCode}");
                using (var answer = JsonDocument.Parse(await asked.Content.ReadAsStreamAsync()))
                {
                    if (string.IsNullOrWhiteSpace(answer.RootElement.GetProperty("answer").GetString()) ||
                        answer.RootElement.GetProperty("evidence").GetArrayLength() == 0)
                        throw new InvalidOperationException("Ask Playback returned no source-backed answer");
                }
                var batches = Environment.GetEnvironmentVariable("PLAYBACK_RETAIN_LIVE_TEST") == "yes" ? 12 : 1;
                var noteCalls = 0;
                for (var batch = 0; batch < batches; batch++)
                {
                    using var state = await ReadJson(http, $"sessions/{sessionId}");
                    if (state.RootElement.GetProperty("transcripts").EnumerateArray().All(x => x.GetProperty("noteStatus").GetString() is "completed" or "deferred" or "suppressed")) break;
                    using var notes = await http.PostAsync($"sessions/{sessionId}/notes/generate", null);
                    if (!notes.IsSuccessStatusCode) throw new InvalidOperationException($"AI notes returned HTTP {(int)notes.StatusCode}");
                    noteCalls++;
                }
                using var final = await ReadJson(http, $"sessions/{sessionId}");
                if (string.IsNullOrWhiteSpace(final.RootElement.GetProperty("noteMarkdown").GetString()))
                    throw new InvalidOperationException("AI notes returned no saved Markdown");
                var statuses = final.RootElement.GetProperty("transcripts").EnumerateArray()
                    .GroupBy(x => x.GetProperty("noteStatus").GetString() ?? "unknown")
                    .ToDictionary(x => x.Key, x => x.Count());
                if (Environment.GetEnvironmentVariable("PLAYBACK_LIVE_ARTIFACT_FOLDER") is {Length: > 0} artifacts)
                {
                    Directory.CreateDirectory(artifacts);
                    await File.WriteAllTextAsync(Path.Combine(artifacts, "session.json"), final.RootElement.GetRawText());
                    await File.WriteAllTextAsync(Path.Combine(artifacts, "generated-notes.md"), final.RootElement.GetProperty("noteMarkdown").GetString());
                    await File.WriteAllTextAsync(Path.Combine(artifacts, "live-result.json"), JsonSerializer.Serialize(new {sessionId, source = "Repository Week 3 first 20 minutes", asrChunks = saved.Length, recognized, noteCalls, maxNoteCalls = batches, noteStatuses = statuses, askCalls = 1, semanticQualityAccepted = false}));
                }
                if (Environment.GetEnvironmentVariable("PLAYBACK_RETAIN_LIVE_TEST") == "yes" && statuses.Any(x => x.Key is not ("completed" or "deferred" or "suppressed")))
                    throw new InvalidOperationException("Bounded live generation left pending or failed sources; inspect retained coverage artifacts before accepting the run");
                Console.WriteLine("Sample audio live AI: source-backed Ask Playback answer and saved notes passed; no lecture text printed.");
            }
            finally { snapshot?.Dispose(); }
        }
        finally
        {
            if (sessionId is not null && Environment.GetEnvironmentVariable("PLAYBACK_RETAIN_LIVE_TEST") != "yes")
            {
                using var cleanup = await http.DeleteAsync($"testing/sessions/{sessionId}");
                if (cleanup.StatusCode != System.Net.HttpStatusCode.NoContent)
                    Console.Error.WriteLine($"Sample audio test cleanup returned HTTP {(int)cleanup.StatusCode}; inspect test session {sessionId}");
            }
        }
    }

    static bool Finished(JsonElement chunk) => chunk.GetProperty("status").GetString() is { } status &&
        (ChunkStatus.Finished(status) || status == ChunkStatus.AsrManual);

    static async Task<JsonDocument> ReadJson(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
    }
}
