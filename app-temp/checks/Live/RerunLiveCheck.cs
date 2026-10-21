using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Paid, end to end against an isolated test API (playback_e2e, port 5082): uploads the Week 3 sample as 150 eight-second
// chunks, waits for first-pass ASR, writes notes from it (notes-only rebuild), reruns ASR in longer windows with incremental
// notes, and measures word error rate of both passes against the first 20 minutes of transcript.txt.
// --rerun-live-ask asks fixed questions on the same session and records which retrieval path chose the passages.
static class RerunLiveCheck
{
    const string Origin = "http://127.0.0.1:5082";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public static readonly string[] Questions =
    [
        "What does the lecturer want students to do with AI-generated answers?",
        "Which networking or security concepts are explained in this part of the lecture?",
        "What example did the lecturer use to illustrate the first posting?"
    ];

    static HttpClient Client()
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_RERUN_LIVE") != "yes") throw new InvalidOperationException("Rerun live test requires explicit PLAYBACK_RERUN_LIVE=yes");
        return new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(Origin + "/api/"), Timeout = TimeSpan.FromMinutes(5) };
    }

    public static async Task Run(string folder, string output)
    {
        using var http = Client();
        Directory.CreateDirectory(output);
        using (var health = JsonDocument.Parse(await http.GetStringAsync("health")))
        {
            var status = health.RootElement;
            if (status.GetProperty("database").GetString() != "playback_e2e" || !status.GetProperty("mongo").GetBoolean() ||
                !status.GetProperty("gemini").GetBoolean() || !status.GetProperty("automaticAsr").GetBoolean())
                throw new InvalidOperationException("Live API must use playback_e2e with MongoDB, Gemini and ASR ready");
        }
        var report = new Dictionary<string, object?>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var create = await http.PostAsJsonAsync("sessions", new { title = $"Rerun live Week 3 {DateTime.UtcNow:yyyy-MM-dd HH:mm}" });
        create.EnsureSuccessStatusCode();
        var sessionId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        report["sessionId"] = sessionId;
        await File.WriteAllTextAsync(Path.Combine(output, "session.txt"), sessionId);
        (await http.PutAsJsonAsync($"sessions/{sessionId}/languages", new { asrLanguage = "en", noteLanguage = "zh-Hant", asrModel = (string?)null })).EnsureSuccessStatusCode();

        var start = DateTime.UtcNow.AddMinutes(-25);
        foreach (var (sequence, wav, _) in SampleAudio.Chunks(folder, 8))
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(sessionId), "sessionId");
            form.Add(new StringContent("system"), "sourceId");
            form.Add(new StringContent(sequence.ToString()), "sequence");
            form.Add(new StringContent((sequence * 8_000).ToString()), "startMs");
            form.Add(new StringContent(((sequence + 1) * 8_000).ToString()), "endMs");
            form.Add(new StringContent(Convert.ToHexString(SHA256.HashData(wav)).ToLowerInvariant()), "sha256");
            form.Add(new StringContent(start.AddSeconds(sequence * 8).ToString("O")), "recordedAt");
            var content = new ByteArrayContent(wav); content.Headers.ContentType = new("audio/wav");
            form.Add(content, "file", "lecture.wav");
            using var uploaded = await http.PostAsync("chunks", form);
            if (uploaded.StatusCode != System.Net.HttpStatusCode.Accepted) throw new InvalidOperationException($"Chunk {sequence} upload returned HTTP {(int)uploaded.StatusCode}");
        }
        Console.WriteLine("Rerun live: 150 eight-second chunks saved; waiting for first-pass ASR.");
        await Until(async () =>
        {
            using var session = JsonDocument.Parse(await http.GetStringAsync($"sessions/{sessionId}"));
            var chunks = session.RootElement.GetProperty("chunks").EnumerateArray().ToArray();
            var done = chunks.Count(x => x.GetProperty("status").GetString() is "transcribed" or "asr-empty" or "silent" or "vad-silence" or "asr-manual");
            return (done == 150, $"{done}/150 first-pass chunks finished");
        }, TimeSpan.FromMinutes(25));
        report["firstPassSeconds"] = clock.Elapsed.TotalSeconds;
        var firstPass = await Transcript(http, sessionId);
        await File.WriteAllTextAsync(Path.Combine(output, "first-pass.txt"), firstPass.Text);

        clock.Restart();
        report["initialNotes"] = await Rerun(http, sessionId, new { asr = false, notes = "rebuild", windowSeconds = 45 }, TimeSpan.FromMinutes(40));
        report["initialNotesSeconds"] = clock.Elapsed.TotalSeconds;
        var notesBefore = await Notes(http, sessionId);
        await File.WriteAllTextAsync(Path.Combine(output, "notes-first-pass.md"), notesBefore.Markdown);

        clock.Restart();
        report["rerun"] = await Rerun(http, sessionId, new { asr = true, asrModel = (string?)null, notes = "incremental", windowSeconds = 45 }, TimeSpan.FromMinutes(60));
        report["rerunSeconds"] = clock.Elapsed.TotalSeconds;
        var rerun = await Transcript(http, sessionId);
        await File.WriteAllTextAsync(Path.Combine(output, "rerun.txt"), rerun.Text);
        var notesAfter = await Notes(http, sessionId);
        await File.WriteAllTextAsync(Path.Combine(output, "notes-rerun.md"), notesAfter.Markdown);

        var reference = Reference(Path.Combine(folder, "transcript.txt"), TimeSpan.FromMinutes(20));
        report["referenceWords"] = Words(reference, false).Length;
        report["firstPass"] = new { rows = firstPass.Rows, words = Words(firstPass.Text, false).Length,
            wer = Wer(Words(reference, false), Words(firstPass.Text, false)), werWithoutFillers = Wer(Words(reference, true), Words(firstPass.Text, true)) };
        report["afterRerun"] = new { rows = rerun.Rows, windows = rerun.Windows, words = Words(rerun.Text, false).Length,
            wer = Wer(Words(reference, false), Words(rerun.Text, false)), werWithoutFillers = Wer(Words(reference, true), Words(rerun.Text, true)) };
        report["notes"] = new { firstPassVersion = notesBefore.Version, rerunVersion = notesAfter.Version,
            firstPassChars = notesBefore.Markdown.Length, rerunChars = notesAfter.Markdown.Length };
        report["activity"] = await Activity(http, sessionId, ["ASR rerun window", "Session rerun", "Note revision", "ASR transcription"]);
        await File.WriteAllTextAsync(Path.Combine(output, "rerun-live.json"), JsonSerializer.Serialize(report, Json));
        Console.WriteLine($"Rerun live finished; report in {output}");
    }

    public static async Task Ask(string sessionId, string output, string label)
    {
        using var http = Client();
        if (label == "graph")
        {
            (await http.PostAsync($"sessions/{sessionId}/concepts/index", null)).EnsureSuccessStatusCode();
            Console.WriteLine("Rerun live: concept graph index requested; waiting for the sidecar.");
            await Until(async () =>
            {
                var entries = await ActivityRows(http, sessionId);
                var index = entries.Where(x => x.Task == "Concept graph index").ToList();
                return (index.Any(x => x.Status is "completed" or "failed") && index.All(x => x.Status is "completed" or "failed"),
                    $"{index.Count} index runs; latest {index.LastOrDefault()?.Status ?? "not started"}");
            }, TimeSpan.FromMinutes(40));
        }
        var answers = new List<object>();
        foreach (var question in Questions)
        {
            using var response = await http.PostAsJsonAsync($"sessions/{sessionId}/ask", new { question, requestId = Guid.NewGuid().ToString() });
            var body = await response.Content.ReadAsStringAsync();
            answers.Add(new { question, status = (int)response.StatusCode, answer = body.Length > 6000 ? body[..6000] : body });
        }
        var result = new Dictionary<string, object?> { ["label"] = label, ["answers"] = answers,
            ["activity"] = await Activity(http, sessionId, ["Concept graph index", "Concept graph query", "Ask Playback"]) };
        if (label == "graph")
        {
            using var concepts = await http.GetAsync($"sessions/{sessionId}/concepts?limit=60");
            result["concepts"] = JsonSerializer.Deserialize<JsonElement>(await concepts.Content.ReadAsStringAsync());
        }
        await File.WriteAllTextAsync(Path.Combine(output, $"ask-{label}.json"), JsonSerializer.Serialize(result, Json));
        Console.WriteLine($"Rerun live: {label} answers saved.");
    }

    static async Task<object> Rerun(HttpClient http, string sessionId, object request, TimeSpan limit)
    {
        using var started = await http.PostAsJsonAsync($"sessions/{sessionId}/rerun", request);
        if (!started.IsSuccessStatusCode) throw new InvalidOperationException($"Rerun start returned HTTP {(int)started.StatusCode}: {await started.Content.ReadAsStringAsync()}");
        JsonElement job = default;
        await Until(async () =>
        {
            job = await http.GetFromJsonAsync<JsonElement>($"sessions/{sessionId}/rerun");
            return (job.GetProperty("status").GetString() != "running", job.GetProperty("summary").GetString() ?? "");
        }, limit);
        if (job.GetProperty("status").GetString() != "completed") throw new InvalidOperationException("Rerun did not complete: " + job.GetRawText());
        return job;
    }

    static async Task Until(Func<Task<(bool Done, string Progress)>> poll, TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit; var reported = DateTime.MinValue;
        while (DateTime.UtcNow < deadline)
        {
            var (done, progress) = await poll();
            if (done) return;
            if (DateTime.UtcNow - reported > TimeSpan.FromSeconds(30)) { Console.WriteLine("Rerun live: " + progress); reported = DateTime.UtcNow; }
            await Task.Delay(5_000);
        }
        throw new TimeoutException("Rerun live step exceeded its time limit");
    }

    static async Task<(string Text, int Rows, int Windows)> Transcript(HttpClient http, string sessionId)
    {
        using var session = JsonDocument.Parse(await http.GetStringAsync($"sessions/{sessionId}"));
        var rows = session.RootElement.GetProperty("transcripts").EnumerateArray()
            .OrderBy(x => x.GetProperty("startMs").GetInt64()).ToList();
        string Text(JsonElement x) => x.TryGetProperty("displayOriginal", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()! : x.GetProperty("original").GetString()!;
        return (string.Join("\n", rows.Select(Text).Where(x => x.Length > 0)), rows.Count,
            rows.Count(x => x.TryGetProperty("kind", out var k) && k.GetString() == "window"));
    }

    static async Task<(string Markdown, int Version)> Notes(HttpClient http, string sessionId)
    {
        using var session = JsonDocument.Parse(await http.GetStringAsync($"sessions/{sessionId}"));
        return (session.RootElement.GetProperty("noteMarkdown").GetString() ?? "", session.RootElement.GetProperty("noteVersion").GetInt32());
    }

    sealed record Row(string Task, string Status, string? Summary, string? Model, long? DurationMs, string? UsageJson, List<string> SourceIds);
    static async Task<List<Row>> ActivityRows(HttpClient http, string sessionId)
    {
        var json = await http.GetFromJsonAsync<JsonElement>($"sessions/{sessionId}/activity");
        var items = json.ValueKind == JsonValueKind.Array ? json : json.GetProperty("items");
        string? S(JsonElement x, string name) => x.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        return items.EnumerateArray().Select(x => new Row(S(x, "task") ?? "", S(x, "status") ?? "", S(x, "summary"), S(x, "model"),
            x.TryGetProperty("durationMs", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt64() : null, S(x, "usageJson"),
            x.TryGetProperty("sourceIds", out var s) && s.ValueKind == JsonValueKind.Array ? s.EnumerateArray().Select(v => v.GetString() ?? "").ToList() : [])).ToList();
    }

    static async Task<object> Activity(HttpClient http, string sessionId, string[] tasks)
    {
        var rows = (await ActivityRows(http, sessionId)).Where(x => tasks.Contains(x.Task)).ToList();
        return rows.GroupBy(x => x.Task).ToDictionary(g => g.Key, g => (object)new
        {
            count = g.Count(), statuses = g.GroupBy(x => x.Status).ToDictionary(s => s.Key, s => s.Count()),
            totalSeconds = g.Sum(x => x.DurationMs ?? 0) / 1000.0, models = g.Select(x => x.Model).Distinct().ToList(),
            samples = g.Take(3).Select(x => new { x.Status, x.Summary, sources = x.SourceIds.Count }).ToList(),
            usage = g.Select(x => x.UsageJson).Where(x => x is not null).Take(3).ToList()
        });
    }

    // transcript.txt alternates a text line and the timestamp at which the next line starts; the first line starts at 0:00.
    public static string Reference(string path, TimeSpan through)
    {
        var text = new StringBuilder();
        foreach (var line in File.ReadAllLines(path))
        {
            var time = Regex.Match(line.Trim(), @"^(?:(\d+):)?(\d+):(\d\d)$");
            if (time.Success)
            {
                var at = new TimeSpan(time.Groups[1].Success ? int.Parse(time.Groups[1].Value) : 0, int.Parse(time.Groups[2].Value), int.Parse(time.Groups[3].Value));
                if (at >= through) break;
                continue;
            }
            text.AppendLine(line);
        }
        return text.ToString();
    }

    static readonly HashSet<string> Fillers = ["um", "uh", "er", "ah", "erm", "hmm", "mm", "oh"];
    public static string[] Words(string text, bool withoutFillers) => Regex.Matches(text.ToLowerInvariant(), @"[\p{L}\p{N}']+")
        .Select(x => x.Value.Trim('\'')).Where(x => x.Length > 0 && (!withoutFillers || !Fillers.Contains(x))).ToArray();

    // Word error rate: word-level edit distance over the reference length.
    public static double Wer(string[] reference, string[] hypothesis)
    {
        var previous = new int[hypothesis.Length + 1]; var current = new int[hypothesis.Length + 1];
        for (var j = 0; j <= hypothesis.Length; j++) previous[j] = j;
        for (var i = 1; i <= reference.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= hypothesis.Length; j++)
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + (reference[i - 1] == hypothesis[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return Math.Round(previous[hypothesis.Length] / (double)Math.Max(1, reference.Length), 4);
    }
}
