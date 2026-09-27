using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

internal static class Week3LiveCheck
{
    public static async Task Run(string folder)
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri("http://127.0.0.1:5078/api/"),
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
            using var create = await http.PostAsJsonAsync("sessions", new { title = $"E2E demo Week3 {Guid.NewGuid():N}" });
            create.EnsureSuccessStatusCode();
            using (var created = JsonDocument.Parse(await create.Content.ReadAsStreamAsync()))
                sessionId = created.RootElement.GetProperty("id").GetString();
            if (sessionId is null || sessionId.Length != 32) throw new InvalidOperationException("Test session was not created");

            foreach (var (sequence, wav, _) in Week3AudioPreview.Chunks(folder))
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
            Console.WriteLine("Week 3 live: 40 WAV chunks saved locally; waiting for SenseVoice results.");

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
                    var done = chunks.Count(chunk => chunk.GetProperty("status").GetString() is
                        "transcribed" or "asr-empty" or "silent" or "asr-manual");
                    if (done == 40) break;
                    if (DateTime.UtcNow - lastReport > TimeSpan.FromSeconds(30))
                    {
                        Console.WriteLine($"Week 3 live: {done}/40 ASR chunks finished.");
                        lastReport = DateTime.UtcNow;
                    }
                    await Task.Delay(5_000);
                }
                if (snapshot is null) throw new InvalidOperationException("No session result was returned");
                var saved = snapshot.RootElement.GetProperty("chunks").EnumerateArray().ToArray();
                if (saved.Length != 40 || saved.Any(chunk => chunk.GetProperty("status").GetString() is
                    not ("transcribed" or "asr-empty" or "silent" or "asr-manual")))
                    throw new TimeoutException("ASR did not finish all 40 saved chunks in 15 minutes");
                var recognized = saved.Count(chunk => chunk.GetProperty("status").GetString() == "transcribed");
                var manual = saved.Count(chunk => chunk.GetProperty("status").GetString() == "asr-manual");
                Console.WriteLine($"Week 3 live ASR: {recognized} transcribed, {manual} require manual retry, {40 - recognized - manual} empty or silent.");
                var source = snapshot.RootElement.GetProperty("transcripts").EnumerateArray()
                    .FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.GetProperty("original").GetString()));
                if (source.ValueKind == JsonValueKind.Undefined)
                    throw new InvalidOperationException("SenseVoice returned no usable transcript for AI verification");
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
                using var notes = await http.PostAsync($"sessions/{sessionId}/notes/generate", null);
                if (!notes.IsSuccessStatusCode)
                    throw new InvalidOperationException($"AI notes returned HTTP {(int)notes.StatusCode}");
                using var final = await ReadJson(http, $"sessions/{sessionId}");
                if (string.IsNullOrWhiteSpace(final.RootElement.GetProperty("noteMarkdown").GetString()))
                    throw new InvalidOperationException("AI notes returned no saved Markdown");
                Console.WriteLine("Week 3 live AI: source-backed Ask Playback answer and saved notes passed; no lecture text printed.");
            }
            finally { snapshot?.Dispose(); }
        }
        finally
        {
            if (sessionId is not null)
            {
                using var cleanup = await http.DeleteAsync($"testing/sessions/{sessionId}");
                if (cleanup.StatusCode != System.Net.HttpStatusCode.NoContent)
                    Console.Error.WriteLine($"Week 3 test cleanup returned HTTP {(int)cleanup.StatusCode}; inspect test session {sessionId}");
            }
        }
    }

    static async Task<JsonDocument> ReadJson(HttpClient http, string path)
    {
        using var response = await http.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
    }
}
