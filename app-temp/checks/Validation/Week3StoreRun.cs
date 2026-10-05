using Playback.Api;
using System.Text.Json;
using MongoDB.Driver;
using Playback.Api.Db;
using Playback.Api.Activity;
using Playback.Api.Terms;
using Playback.Api.Providers;
using Microsoft.Extensions.Logging.Abstractions;

// The Week 3 first hour in local MongoDB (playback_e2e): Seed imports the exact transcript text once and keeps
// the session; Term ranks and explains one real term with Jev and Gemini (paid, PLAYBACK_WEEK3_LIVE=yes).
static class Week3StoreRun
{
    public static async Task Term(string folder) {
        if (Environment.GetEnvironmentVariable("PLAYBACK_WEEK3_LIVE") != "yes") throw new InvalidOperationException("Explicit live opt-in required");
        Environment.SetEnvironmentVariable("PLAYBACK_MONGO_URI", "mongodb://127.0.0.1:27017");
        Environment.SetEnvironmentVariable("PLAYBACK_MONGO_DATABASE", "playback_e2e");
        Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "no");
        Environment.SetEnvironmentVariable("PLAYBACK_AUTO_TERMS", "no");
        using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "db-session.json")));
        var id = saved.RootElement.GetProperty("id").GetString()!;
        var store = new PlaybackStore(); var session = (await store.Session(id))!;
        var termName = Environment.GetEnvironmentVariable("PLAYBACK_WEEK3_TERM") ?? "CPU";
        if (termName is not ("CPU" or "Microsoft Symbol Server")) throw new InvalidOperationException("Unexpected validation term");
        var candidate = session.Terms.First(x => x.Text.Equals(termName, StringComparison.Ordinal));
        var activity = new AiActivity(store); using var transport = new JevTransport(); var classifier = new JevTermClassifier(transport);
        var model = new GeminiLanguageModel();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var record = await activity.Begin(id, "Jev ranking: " + candidate.Text, "typesafe", "discovery", candidate.TranscriptIds.Concat(candidate.MaterialIds));
        activity.Context(record, "term-rank-v1", JsonSerializer.Serialize(JevTermClassifier.Questions), JevTermClassifier.State(candidate.Text, candidate.Context));
        await activity.Start(record);
        TermInsight? insight = null; string? error = null;
        try {
            var rank = await classifier.Rank(candidate.Text, deadline.Token, candidate.Context);
            record.UsageJson = rank.Usage.GetRawText(); record.ProviderLatencyMs = rank.LatencyMs;
            insight = await store.SaveTermRanking(id, candidate, rank, session.NoteLanguage);
            await activity.End(record, "completed", $"Highlight={insight.Highlight}; explain probability={rank.JevProbability}", rank.Model);
            if (!insight.Highlight) throw new InvalidOperationException("Real Jev did not select this technical term; no explanation forced");
            await using var reviewer = new TermReviewAgent(store, classifier, model, NullLogger<TermReviewAgent>.Instance, activity);
            insight = await reviewer.Explain(id, insight.Id, deadline.Token);
        } catch (Exception ex) {
            error = AiActivity.SafeError(ex);
            if (record.Status is "queued" or "running") await activity.Fail(record, ex);
        }
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(folder, "term-result.json"), JsonSerializer.Serialize(new {
            testedAt = DateTime.UtcNow, status = error is null ? "completed" : "failed", error, term = candidate.Text,
            insight, records = (await activity.Read(id)).Where(x => x.Task.StartsWith("Jev ranking:") || x.Task.StartsWith("Term explanation:")).ToList(),
            evidence = "Live Jev and Google Search through production providers and real MongoDB. No audio."
        }, options));
        Console.WriteLine($"Week 3 term validation: {(error is null ? "completed" : "failed")}; inspect saved usage and safe result.");
        if (error is not null) Environment.ExitCode = 1;
    }
    public static async Task Seed(string folder) {
        const string uri = "mongodb://127.0.0.1:27017";
        Environment.SetEnvironmentVariable("PLAYBACK_MONGO_URI", uri);
        Environment.SetEnvironmentVariable("PLAYBACK_MONGO_DATABASE", "playback_e2e");
        Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "yes");
        var store = new PlaybackStore();
        if (!await store.IsReady()) throw new InvalidOperationException("Local test MongoDB must be ready before seeding");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, options);
        var sessionFile = Path.Combine(folder, "db-session.json");
        if (File.Exists(sessionFile)) {
            using var saved = JsonDocument.Parse(await File.ReadAllTextAsync(sessionFile));
            if (await store.Session(saved.RootElement.GetProperty("id").GetString()!) is not null) {
                Console.WriteLine("Existing retained Week 3 DB session reused; no source imports repeated."); return;
            }
            throw new InvalidOperationException("Saved test session is unavailable; do not silently replace its identity");
        }
        var text = await File.ReadAllTextAsync("app-temp/data/test-audio/sampleAudio/transcript.txt");
        using var provenance = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "input-provenance.json")));
        if (provenance.RootElement.GetProperty("sourceHash").GetString() != ContentHash.Of(text))
            throw new InvalidOperationException("Week 3 provenance does not match repository transcript");
        var passages = provenance.RootElement.GetProperty("rawPassages").EnumerateArray().ToArray();
        if (passages.Length != 409 || passages.Any(x => x.GetProperty("startMs").GetInt64() < 0 || x.GetProperty("endMs").GetInt64() > 3600000))
            throw new InvalidOperationException("Unexpected first-hour transcript provenance");
        var group = Json(await store.CreateGroup("E2E Week 3 live " + Guid.NewGuid().ToString("N")[..8]));
        var groupId = group.GetProperty("id").GetString()!;
        var title = "E2E demo Week 3 first hour " + Guid.NewGuid().ToString("N")[..8];
        var created = Json(await store.CreateSession(title, groupId)); var id = created.GetProperty("id").GetString()!;
        await store.SetLanguages(id, "en", "zh-Hant");
        var epoch = DateTime.UtcNow.Date;
        var records = passages.Chunk(3).Select((parts, index) => new Transcript {
            Id = id + "_text_" + index, SessionId = id, SourceId = "week3-text",
            StartMs = parts[0].GetProperty("startMs").GetInt64(), EndMs = parts[^1].GetProperty("endMs").GetInt64(),
            RecordedAt = epoch.AddMilliseconds(parts[0].GetProperty("startMs").GetInt64()),
            Original = string.Join(" ", parts.Select(x => x.GetProperty("original").GetString())), NoteStatus = "pending"
        }).ToList();
        var db = new MongoClient(uri).GetDatabase("playback_e2e");
        await db.GetCollection<Transcript>("transcripts").InsertManyAsync(records);
        await File.WriteAllTextAsync(Path.Combine(folder, "db-input-provenance.json"), JsonSerializer.Serialize(new {
            sourceHash = ContentHash.Of(text), fromMs = 0, throughMs = 3600000, rawPassages = passages,
            textImportSources = records, interpretation = "Exact Week 3 first-hour text, three neighboring paragraphs per import. No audio or ASR claim."
        }, options));
        await File.WriteAllTextAsync(sessionFile, JsonSerializer.Serialize(new {
            id, title, groupId, groupName = group.GetProperty("name").GetString(), database = "playback_e2e",
            sourceHash = ContentHash.Of(text), rawPassages = passages.Length, textImportSources = records.Count, createdAt = DateTime.UtcNow
        }, options));
        Console.WriteLine($"Week 3 source text retained in playback_e2e: {id}; {passages.Length} original paragraphs, {records.Count} exact text imports. No audio upload.");
    }
}
