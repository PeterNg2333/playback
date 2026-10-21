using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Api.Providers;
using Playback.Application.Activity;
using Playback.Application.Notes;
using Playback.Domain;
using Playback.Domain.Models;

// Three-speed notes on the whole Week 3 lecture through the product's NoteWorkflow, real MongoDB (playback_e2e) and the
// real note writer (Qwen3.8 Flash on OpenRouter, then GLM, then Gemini). Text only; no audio is read or sent.
// Phases, each saving <phase>.json in the run folder (three-speed-live.mjs runs them and reuses saved results):
//   seed     a session with the course glossary as its material; no transcript yet
//   record   the captions arrive as they were spoken (RecordedAt from the caption clock, ConfirmedAt a few seconds later)
//            on a simulated clock; the scheduler jots each finished minute and writes each closed five minutes; then Stop
//   edit     a second-pass correction of one line, then the notes-only rerun (WritePending)
//   rebuild  one block locked, then Rebuild notes
//   report   metrics from the database and the call ledger (no provider call)
// Every provider call goes through BudgetWriter, which appends to ledger.jsonl and refuses calls once the folder's
// reported spend reaches PLAYBACK_THREE_SPEED_MAX_USD.
static class ThreeSpeedLiveRun
{
    const string TranscriptFile = "app-temp/data/test-audio/sampleAudio/transcript.txt";
    const string Glossary = "app-temp/data/validation/runs/2026-10-21-poc/notes-qwen3.8-flash_none/course-context.json";
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static readonly JsonSerializerOptions Line = new(Json) { WriteIndented = false };

    static bool Dry => Environment.GetEnvironmentVariable("PLAYBACK_THREE_SPEED_DRY") == "yes";

    static IMongoCollection<Transcript> Transcripts() =>
        new MongoClient("mongodb://127.0.0.1:27017").GetDatabase("playback_e2e").GetCollection<Transcript>("transcripts");

    public static async Task Run(string folder, string phase)
    {
        Environment.SetEnvironmentVariable("PLAYBACK_MONGO_URI", "mongodb://127.0.0.1:27017");
        Environment.SetEnvironmentVariable("PLAYBACK_MONGO_DATABASE", "playback_e2e");
        // A dry run answers every call with the fixture note model: the same phases on MongoDB, no network.
        var paid = phase is "record" or "edit" or "rebuild" && !Dry;
        if (paid && Environment.GetEnvironmentVariable("PLAYBACK_THREE_SPEED_LIVE") != "yes")
            throw new InvalidOperationException("Explicit live opt-in required (PLAYBACK_THREE_SPEED_LIVE=yes)");
        Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", paid ? "no" : "yes");
        Directory.CreateDirectory(folder);
        var store = new PlaybackStore();
        if (!await store.IsReady()) throw new InvalidOperationException("Local MongoDB (playback_e2e) is not ready; run pnpm.cmd db:up");
        switch (phase)
        {
            case "seed": await Seed(store, folder); break;
            case "record": await Record(store, folder); break;
            case "edit": await Edit(store, folder); break;
            case "rebuild": await Rebuild(store, folder); break;
            case "report": await Report(store, folder); break;
            default: throw new InvalidOperationException("Unknown phase");
        }
    }

    // ── Captions ──

    sealed record Caption(int Index, long StartMs, long EndMs, string Text);

    // The same reading as the 2026-10-21 POC (week3Lines): a time line starts the captions after it.
    static List<Caption> Captions(string text)
    {
        var rows = new List<(long At, string Text)>();
        long at = 0;
        foreach (var raw in text.TrimStart('﻿').Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (Regex.IsMatch(line, @"^\d{1,3}:\d{2}(?::\d{2})?$")) { at = line.Split(':').Aggregate(0L, (n, part) => n * 60 + long.Parse(part)) * 1000; continue; }
            rows.Add((at, line));
        }
        return rows.Select((x, i) => new Caption(i, x.At, i + 1 < rows.Count ? rows[i + 1].At : x.At + 5000, x.Text)).ToList();
    }

    // Recognition confirms a line a few seconds after it ends; a caption before a long pause is taken to last 15 s at most.
    static DateTime Confirmed(DateTime epoch, Caption x) => epoch.AddMilliseconds(x.StartMs + Math.Min(x.EndMs - x.StartMs, 15_000) + 3_000);

    static Transcript Row(string id, DateTime epoch, Caption x) => new()
    {
        Id = $"{id}_w3_{x.Index:D4}", SessionId = id, SourceId = "week3-text", StartMs = x.StartMs, EndMs = x.EndMs,
        RecordedAt = epoch.AddMilliseconds(x.StartMs), Original = x.Text, NoteStatus = "pending", ConfirmedAt = Confirmed(epoch, x)
    };

    // ── Seed ──

    static async Task Seed(PlaybackStore store, string folder)
    {
        var file = Path.Combine(folder, "seed.json");
        if (File.Exists(file))
        {
            var saved = JsonNode.Parse(await File.ReadAllTextAsync(file))!;
            if (await store.Session((string)saved["id"]!) is null) throw new InvalidOperationException("The saved session is gone; start a new run folder");
            Console.WriteLine("Seed reused: " + saved["id"]);
            return;
        }
        await store.EnsureRecoveryIndexes(CancellationToken.None);
        var text = await File.ReadAllTextAsync(TranscriptFile);
        var captions = Captions(text);
        var glossary = await File.ReadAllTextAsync(Glossary);
        var group = await store.CreateGroup("E2E three-speed live " + DateTime.UtcNow.ToString("yyyy-MM-dd"));
        var title = "E2E Week 3 three-speed " + Guid.NewGuid().ToString("N")[..6];
        var id = JsonSerializer.SerializeToElement(await store.CreateSession(title, group.Id), Json).GetProperty("id").GetString()!;
        await store.SetLanguages(id, "en", "zh-Hant");
        // The POC's course context (subject and glossary built from Tutorial.txt) is the material, so every call starts with it.
        await store.AddMaterial(id, "Week 3 course glossary (from Tutorial.txt)", glossary);
        await Write(file, new
        {
            id, title, groupId = group.Id, database = "playback_e2e", transcriptHash = ContentHash.Of(text), captions = captions.Count,
            lectureMs = captions[^1].EndMs, material = "course-context.json from 2026-10-21-poc (Qwen3.8 run)", noteLanguage = "zh-Hant",
            evidence = "Exact Week 3 captions, one transcript row per caption line, inserted during the record phase. No audio."
        });
        Console.WriteLine($"Seeded {title} ({id}) in playback_e2e: {captions.Count} caption lines to replay, glossary material.");
    }

    // ── Shared run state ──

    sealed class SimClock(DateTime start) : TimeProvider
    {
        long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(start, TimeSpan.Zero).AddTicks(ticks);
        public DateTime Now => GetUtcNow().UtcDateTime;
        public void Advance(TimeSpan by) => ticks += by.Ticks;
        public void AtLeast(DateTime at) { if (at > Now) ticks = (at - start).Ticks; }
    }

    sealed record LiveRun(PlaybackStore Store, string Id, SimClock Clock, BudgetWriter Writer, NoteWorkflow Notes, NoteStoreAdapter Adapter, string Folder);

    static async Task<LiveRun> Open(PlaybackStore store, string folder, string phase, DateTime simStart)
    {
        var seed = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "seed.json")))!;
        var id = (string)seed["id"]!;
        var clock = new SimClock(simStart);
        var max = double.Parse(Environment.GetEnvironmentVariable("PLAYBACK_THREE_SPEED_MAX_USD") ?? "0.3", System.Globalization.CultureInfo.InvariantCulture);
        var writer = new BudgetWriter(Dry ? new GeminiNoteWriter(new FixtureNoteModel()) : NoteWriters.Create(new GeminiLanguageModel()), Path.Combine(folder, "ledger.jsonl"), phase, max, clock, simStart);
        if (!writer.IsConfigured) throw new InvalidOperationException("No note writer is configured (OPENROUTER_API_KEY)");
        var adapter = new NoteStoreAdapter(store);
        var notes = new NoteWorkflow(adapter, adapter, writer,
            new NoteActivityAdapter(new AiActivity(store), NullLogger<NoteAgent>.Instance), new OpenccNoteScript(), clock);
        return new(store, id, clock, writer, notes, adapter, folder);
    }

    // Ticks the scheduler as the API's worker does; keeps ticking while the last tick called the writer.
    static async Task Drive(LiveRun run, int maxTicks = 8)
    {
        for (var i = 0; i < maxTicks; i++)
        {
            run.Writer.ThrowIfExhausted();
            var calls = run.Writer.Calls;
            run.Clock.Advance(TimeSpan.FromSeconds(2.1));
            run.Notes.Tick([run.Id], CancellationToken.None);
            await run.Notes.Drain();
            if (run.Writer.Calls == calls) return;
        }
    }

    static async Task<Note> Latest(LiveRun run) => (await run.Adapter.Session(run.Id))!.CurrentNote ?? throw new InvalidOperationException("No notes yet");

    // ── Record and Stop ──

    static async Task Record(PlaybackStore store, string folder)
    {
        var file = Path.Combine(folder, "record.json");
        if (File.Exists(file)) { Console.WriteLine("Record reused; delete record.json to pay for it again."); return; }
        var captions = Captions(await File.ReadAllTextAsync(TranscriptFile));
        var transcripts = Transcripts();
        var seed = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "seed.json")))!;
        var id = (string)seed["id"]!;
        // An interrupted record phase resumes: the epoch is the first saved line's RecordedAt.
        var first = await transcripts.Find(x => x.SessionId == id).SortBy(x => x.StartMs).FirstOrDefaultAsync();
        var epoch = first?.RecordedAt ?? DateTime.UnixEpoch.AddSeconds((long)(DateTime.UtcNow.AddHours(-3) - DateTime.UnixEpoch).TotalSeconds);
        var run = await Open(store, folder, "record", epoch);
        var saved = (await transcripts.Find(x => x.SessionId == id).Project(x => x.Id).ToListAsync()).ToHashSet();
        var rows = captions.Select(x => Row(id, epoch, x)).ToList();
        var waiting = new Queue<Transcript>(rows.Where(x => !saved.Contains(x.Id)).OrderBy(x => x.ConfirmedAt));
        if (saved.Count > 0) run.Clock.AtLeast(rows.Where(x => saved.Contains(x.Id)).Max(x => x.ConfirmedAt!.Value));
        var watch = Stopwatch.StartNew();
        var timeline = new List<object>();
        var minute = -1;
        while (waiting.Count > 0)
        {
            run.Clock.AtLeast(run.Clock.Now.AddSeconds(10));
            var batch = new List<Transcript>();
            while (waiting.Count > 0 && waiting.Peek().ConfirmedAt <= run.Clock.Now) batch.Add(waiting.Dequeue());
            if (batch.Count > 0) await transcripts.InsertManyAsync(batch);
            await Drive(run);
            var now = (int)((run.Clock.Now - epoch).TotalMinutes);
            if (now != minute && now % 15 == 0)
            {
                minute = now;
                Console.WriteLine($"  {now} min simulated, {run.Writer.Calls} calls, US${run.Writer.Spent:0.0000} spent, {watch.Elapsed:mm\\:ss} real");
            }
        }
        var beforeStop = await Latest(run);
        timeline.Add(new { at = "before stop", version = beforeStop.Version, segments = beforeStop.Segments.Count,
            live = beforeStop.Sections.FirstOrDefault(x => x.Id == NoteLayout.LiveTopicId)?.Blocks.Count ?? 0 });

        // Stop: the recorder requests a flush; the next ticks write the open window, then outline and polish.
        await store.RequestNoteFlush(id);
        var stopAt = DateTime.UtcNow;
        var stopWatch = Stopwatch.StartNew();
        var finished = false;
        while (stopWatch.Elapsed < TimeSpan.FromMinutes(20))
        {
            await Drive(run, maxTicks: 1);
            if ((await store.NoteGate(id))?.FlushRequested == false) { finished = true; break; }
            run.Clock.Advance(TimeSpan.FromSeconds(1));
        }
        var stopMs = stopWatch.ElapsedMilliseconds;
        var note = await Latest(run);
        var outline = run.Writer.Entries.Where(x => x.Task == NotePrompts.Outline.Name && x.EndedAt >= stopAt).ToList();
        await Write(file, new
        {
            sessionId = id, epoch, completed = finished, lectureMinutes = Math.Round(captions[^1].EndMs / 60000.0, 1),
            realMinutes = Math.Round(watch.Elapsed.TotalMinutes, 1), calls = run.Writer.Calls, spentUsd = run.Writer.Spent,
            stop = new
            {
                requestedAt = stopAt, acknowledgedMs = finished ? stopMs : (long?)null,
                outlineDoneMs = outline.LastOrDefault() is { } o ? (long)(o.EndedAt - stopAt).TotalMilliseconds : (long?)null,
                callsAfterStop = run.Writer.Entries.Count(x => x.EndedAt >= stopAt)
            },
            timeline, finalVersion = note.Version
        });
        Console.WriteLine($"Record: {(finished ? "Stop finished" : "Stop did NOT finish")} in {stopMs / 1000.0:0.0} s; {run.Writer.Calls} calls, US${run.Writer.Spent:0.0000}.");
        if (!finished) Environment.ExitCode = 1;
    }

    // ── Edit: a corrected line rewrites one window and repolishes only the topics it fed ──

    static async Task Edit(PlaybackStore store, string folder)
    {
        var file = Path.Combine(folder, "edit.json");
        if (File.Exists(file)) { Console.WriteLine("Edit reused."); return; }
        var record = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "record.json")))!;
        var run = await Open(store, folder, "edit", (DateTime)record["epoch"]!);
        run.Clock.AtLeast(DateTime.UtcNow);
        var before = await Latest(run);
        var transcripts = Transcripts();
        // The POC's known misrecognition: the lecturer said create file; the captions say query file.
        var target = (await transcripts.Find(x => x.SessionId == run.Id && x.SupersededBy == null).SortBy(x => x.StartMs).ToListAsync())
            .First(x => Regex.IsMatch(x.Original, "query file", RegexOptions.IgnoreCase));
        var corrected = Regex.Replace(target.Original, "query file", "create file", RegexOptions.IgnoreCase);
        var window = (int)(target.StartMs / NoteTimeline.WindowMs);
        // What TranscriptReviewRepository.Save does for a changed second pass, inside the note lock.
        await run.Notes.UpdateEvidence(run.Id, async () => (await transcripts.UpdateOneAsync(x => x.Id == target.Id,
            Builders<Transcript>.Update.Set(x => x.FirstPassOriginal, target.Original).Set(x => x.Original, corrected)
                .Set(x => x.ReviewStatus, "completed").Set(x => x.NoteStatus, "pending"))).ModifiedCount == 1, CancellationToken.None);
        var watch = Stopwatch.StartNew();
        var written = await run.Notes.WritePending(run.Id, CancellationToken.None);
        var after = await Latest(run);

        var changedSegments = after.Segments.Where(s => before.Segments.FirstOrDefault(b => b.Index == s.Index)?.InputHash != s.InputHash).Select(s => s.Index).ToList();
        var fed = before.Sections.Where(t => NoteLayout.IsFinished(t) && t.SegmentBlockIds.Any(b => NoteLayout.SegmentOf(b) == window)).Select(t => t.Id).ToHashSet();
        string Text(NoteSection t) => t.Title + "\n" + string.Join("\n\n", t.Blocks.OrderBy(b => b.Order).Select(b => b.Body));
        var topics = before.Sections.Select(t => (Before: t, After: after.Sections.FirstOrDefault(x => x.Id == t.Id))).ToList();
        var changed = topics.Where(x => x.After is null || Text(x.Before) != Text(x.After)).Select(x => x.Before.Id).ToList();
        var untouched = topics.Where(x => !fed.Contains(x.Before.Id)).ToList();
        var untouchedSame = untouched.Count(x => x.After is not null && Text(x.Before) == Text(x.After));
        var editCalls = run.Writer.Entries.Where(x => x.Phase == "edit").ToList();
        object Mentions(Note note)
        {
            var text = string.Join("\n", note.Sections.Where(t => fed.Contains(t.Id)).SelectMany(t => t.Blocks).Select(b => b.Body));
            return new { query = Regex.Matches(text, "query ?file", RegexOptions.IgnoreCase).Count, create = Regex.Matches(text, "create ?file", RegexOptions.IgnoreCase).Count };
        }
        var passed = changedSegments.SequenceEqual([window]) && changed.All(fed.Contains) && untouchedSame == untouched.Count;
        await Write(file, new
        {
            passed, target = new { target.Id, clock = NoteTimeline.Clock(target.StartMs), before = target.Original, after = corrected, window },
            windowsWritten = written, changedSegments, topicsFedByWindow = fed, topicsChanged = changed,
            untouchedTopics = untouched.Count, untouchedIdentical = untouchedSame, seconds = Math.Round(watch.Elapsed.TotalSeconds, 1),
            calls = editCalls.Select(x => x.Task), costUsd = editCalls.Sum(x => x.CostUsd),
            // The other lines of the window still say "query file", so the topic can name both after one corrected line.
            fedTopicMentions = new { before = Mentions(before), after = Mentions(after) },
            versions = new { before = before.Version, after = after.Version }
        });
        Console.WriteLine($"Edit: segments rewritten [{string.Join(",", changedSegments)}] (window {window}); topics changed {changed.Count} of {fed.Count} fed; " +
            $"{untouchedSame}/{untouched.Count} other topics identical; US${editCalls.Sum(x => x.CostUsd):0.0000}.");
        if (!passed) Environment.ExitCode = 1;
    }

    // ── Rebuild: a locked block stays ──

    static async Task Rebuild(PlaybackStore store, string folder)
    {
        var file = Path.Combine(folder, "rebuild.json");
        if (File.Exists(file)) { Console.WriteLine("Rebuild reused."); return; }
        var record = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "record.json")))!;
        var run = await Open(store, folder, "rebuild", (DateTime)record["epoch"]!);
        run.Clock.AtLeast(DateTime.UtcNow);
        var before = await Latest(run);
        var topic = before.Sections.Where(NoteLayout.IsFinished).OrderBy(x => x.ParentId is null ? 0 : 1).ThenBy(x => x.Order).Skip(1).First();
        var block = topic.Blocks.OrderBy(x => x.Order).First();
        await new NoteCommands(run.Adapter, run.Adapter).LockBlock(run.Id, before.Version, block.Id, true);
        var watch = Stopwatch.StartNew();
        var result = await run.Notes.Rebuild(run.Id, CancellationToken.None);
        var after = await Latest(run);
        var kept = after.Sections.SelectMany(t => t.Blocks.Select(b => (Topic: t, Block: b))).FirstOrDefault(x => x.Block.Id == block.Id);
        var history = await store.NoteHistory(run.Id);
        var calls = run.Writer.Entries.Where(x => x.Phase == "rebuild").ToList();
        var passed = kept.Block is { UserEdited: true } && kept.Block.Body == block.Body && after.Sections.Where(x => x.Id != NoteLayout.LiveTopicId).All(NoteLayout.IsFinished)
            && history.Any(x => x.Author == "user rebuild");
        await Write(file, new
        {
            passed, locked = new { block.Id, topic = topic.Title, body = block.Body }, keptIn = kept.Topic?.Title, keptIdentical = kept.Block?.Body == block.Body,
            result, seconds = Math.Round(watch.Elapsed.TotalSeconds, 1), calls = calls.Count, costUsd = calls.Sum(x => x.CostUsd),
            topics = new { before = before.Sections.Count, after = after.Sections.Count }
        });
        Console.WriteLine($"Rebuild: locked block {(passed ? "kept" : "NOT kept")} ({kept.Topic?.Title}); {calls.Count} calls, US${calls.Sum(x => x.CostUsd):0.0000}, {watch.Elapsed.TotalSeconds:0} s.");
        if (!passed) Environment.ExitCode = 1;
    }

    // ── Report ──

    static async Task Report(PlaybackStore store, string folder)
    {
        var seed = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "seed.json")))!;
        var id = (string)seed["id"]!;
        var ledger = BudgetWriter.Read(Path.Combine(folder, "ledger.jsonl"));
        var history = (await store.NoteHistory(id)).OrderBy(x => x.Version).ToList();
        var transcripts = await Transcripts().Find(x => x.SessionId == id).ToListAsync();
        var minuteOf = transcripts.ToDictionary(x => x.Id, x => (int)(x.StartMs / 60_000));
        var spoken = transcripts.Select(x => (int)(x.StartMs / 60_000)).ToHashSet();
        var activity = await new AiActivity(store).Read(id);

        // The note after Stop (before the edit), and the final note.
        var record = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "record.json")))!;
        var afterStop = history.First(x => x.Version == (int)record["finalVersion"]!);
        object Shape(Note note)
        {
            var topics = note.Sections.Where(x => x.Id != NoteLayout.LiveTopicId).ToList();
            int Depth(NoteSection t) => t.ParentId is null ? 1 : 1 + Depth(topics.First(x => x.Id == t.ParentId));
            var cited = topics.SelectMany(t => t.Blocks).SelectMany(b => NoteLayout.Sources(b, note.Citations)).Where(minuteOf.ContainsKey).Select(x => minuteOf[x]).ToHashSet();
            var text = Regex.Replace(CitationSyntax.Rewrite(note.Markdown, _ => ""), @"\[\]", "");
            var chars = text.Count(c => !char.IsWhiteSpace(c));
            return new
            {
                version = note.Version, author = note.Author, topics = topics.Count, topLevel = topics.Count(x => x.ParentId is null),
                depth = topics.Count == 0 ? 0 : topics.Max(Depth), blocks = topics.Sum(x => x.Blocks.Count), segments = note.Segments.Count,
                citedMinutes = cited.Count, spokenMinutes = spoken.Count, lectureMinutes = spoken.Max() + 1,
                characters = chars, uncertainMarks = Regex.Matches(text, @"[(（]\?").Count,
                lecturerPer1000 = Math.Round(Regex.Matches(text, "講師|讲师|lecturer", RegexOptions.IgnoreCase).Count * 1000.0 / Math.Max(1, chars), 2),
                summary = note.Summary.Count, reviewQuestions = note.ReviewQuestions.Count,
                titles = topics.Where(x => x.ParentId is null).OrderBy(x => x.Order).Select(t => new { t.Title,
                    children = topics.Where(c => c.ParentId == t.Id).OrderBy(c => c.Order).Select(c => c.Title) })
            };
        }
        object Calls(IEnumerable<BudgetWriter.Entry> entries) => entries.GroupBy(x => x.Task).Select(g => new
        {
            task = g.Key, calls = g.Count(), failed = g.Count(x => x.Error is not null), inputTokens = g.Sum(x => x.InputTokens),
            cachedTokens = g.Sum(x => x.CachedTokens), outputTokens = g.Sum(x => x.OutputTokens), costUsd = Math.Round(g.Sum(x => x.CostUsd), 5),
            medianMs = Median(g.Select(x => x.LatencyMs)), maxMs = g.Max(x => x.LatencyMs)
        }).ToList();
        var phases = ledger.GroupBy(x => x.Phase).ToDictionary(g => g.Key, g => new { calls = g.Count(), costUsd = Math.Round(g.Sum(x => x.CostUsd), 5), byTask = Calls(g) });
        // Would a live session keep up? Each jot and segment before Stop is due at its simulated time; one runs at a time.
        var stopAt = (DateTime)record["stop"]!["requestedAt"]!;
        var recording = ledger.Where(x => x.Phase == "record" && x.SimSeconds is not null && x.EndedAt < stopAt).OrderBy(x => x.SimSeconds).ToList();
        double free = 0; var lags = new List<double>();
        foreach (var x in recording) { var start = Math.Max(free, x.SimSeconds!.Value); free = start + x.LatencyMs / 1000.0; lags.Add(free - x.SimSeconds.Value); }
        var fallbacks = ledger.Where(x => x.Writer is not null || x.Error is not null).Select(x => new { x.Phase, x.Task, x.Model, x.Provider, x.Writer, x.Error });
        var edit = File.Exists(Path.Combine(folder, "edit.json")) ? JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "edit.json"))) : null;
        var rebuild = File.Exists(Path.Combine(folder, "rebuild.json")) ? JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "rebuild.json"))) : null;
        var final = history[^1];
        await Write(Path.Combine(folder, "results.json"), new
        {
            sessionId = id, generatedAt = DateTime.UtcNow, totalCostUsd = Math.Round(ledger.Sum(x => x.CostUsd), 5), totalCalls = ledger.Count,
            tokens = new { input = ledger.Sum(x => x.InputTokens), cached = ledger.Sum(x => x.CachedTokens), output = ledger.Sum(x => x.OutputTokens) },
            phases, afterStop = Shape(afterStop), final = Shape(final), stop = record["stop"], record = new { realMinutes = record["realMinutes"], completed = record["completed"] },
            liveLag = new { calls = recording.Count, medianSeconds = Median(lags.Select(x => (long)x)), maxSeconds = lags.Count == 0 ? 0 : Math.Round(lags.Max(), 1),
                busySeconds = Math.Round(recording.Sum(x => x.LatencyMs) / 1000.0), lectureSeconds = recording.Count == 0 ? 0 : recording.Max(x => x.SimSeconds!.Value) },
            fallbacks, failedActivity = activity.Where(x => x.Status == "failed").Select(x => new { x.Task, x.Model, x.Summary }),
            edit, rebuild,
            passed = (bool?)record["completed"] == true && (bool?)edit?["passed"] == true && (bool?)rebuild?["passed"] == true
        });
        await File.WriteAllTextAsync(Path.Combine(folder, "notes-after-stop.md"), afterStop.Markdown);
        await File.WriteAllTextAsync(Path.Combine(folder, "notes-final.md"), final.Markdown);
        await Write(Path.Combine(folder, "segments.json"), afterStop.Segments.Select(s => new { s.Index, clock = NoteTimeline.Clock(s.StartMs), s.Title,
            blocks = s.Blocks.Count, s.Concepts, s.Corrections }));
        await Write(Path.Combine(folder, "activity.json"), activity.Select(x => new { x.Task, x.Provider, x.Model, x.Status, x.StartedAt, x.DurationMs,
            x.ProviderLatencyMs, x.Summary, x.UsageJson, x.BasedOnVersion }));
        Console.WriteLine($"Report: {ledger.Count} calls, US${ledger.Sum(x => x.CostUsd):0.0000}; results.json written.");
    }

    static long Median(IEnumerable<long> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? 0 : sorted[sorted.Count / 2];
    }

    static Task Write(string path, object value) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Json));

    // ── Budget and ledger ──

    // Wraps the product's writer: records each call's usage (OpenRouter's reported cost; a Gemini fallback is priced at
    // list price) and refuses calls once the folder's spend reaches the cap. Estimated in-flight calls count toward it.
    sealed class BudgetWriter(INoteWriter inner, string ledger, string phase, double maxUsd, SimClock clock, DateTime epoch) : INoteWriter
    {
        public sealed record Entry(string Phase, string Task, DateTime EndedAt, double? SimSeconds, long LatencyMs, string? Model, string? Provider,
            long InputTokens, long CachedTokens, long OutputTokens, double CostUsd, bool CostEstimated, JsonNode? Writer, string? Error);

        const double Reserve = 0.004;
        readonly Lock gate = new();
        readonly List<Entry> entries = Read(ledger);
        int inFlight;
        public bool IsConfigured => inner.IsConfigured;
        public string Provider => inner.Provider;
        public string Model => inner.Model;
        public int Calls { get { lock (gate) return entries.Count(x => x.Phase == phase); } }
        public double Spent { get { lock (gate) return entries.Sum(x => x.CostUsd); } }
        public List<Entry> Entries { get { lock (gate) return entries.ToList(); } }

        public static List<Entry> Read(string path) => File.Exists(path)
            ? File.ReadAllLines(path).Where(x => x.Length > 0).Select(x => JsonSerializer.Deserialize<Entry>(x, Json)!).ToList() : [];

        public void ThrowIfExhausted()
        {
            if (Spent >= maxUsd - Reserve) throw new BudgetExhausted($"Budget US${maxUsd} reached (spent US${Spent:0.0000})");
        }

        public async Task<string> Complete(NoteTask task, string instructions, string input, CancellationToken ct,
            Action<string>? onUpdate = null, Action<string>? onUsage = null)
        {
            lock (gate)
            {
                if (entries.Sum(x => x.CostUsd) + (inFlight + 1) * Reserve > maxUsd) throw new BudgetExhausted("Budget reached; call refused");
                inFlight++;
            }
            var sim = (clock.Now - epoch).TotalSeconds;
            var watch = Stopwatch.StartNew();
            string? usage = null;
            string? error = null;
            try { return await inner.Complete(task, instructions, input, ct, onUpdate, u => { usage = u; onUsage?.Invoke(u); }); }
            catch (Exception ex) { error = AiActivity.SafeError(ex); throw; }
            finally
            {
                var entry = Parse(task.Name, usage, watch.ElapsedMilliseconds, phase == "record" ? sim : null, error);
                lock (gate)
                {
                    inFlight--;
                    entries.Add(entry);
                    File.AppendAllText(ledger, JsonSerializer.Serialize(entry, Line) + "\n");
                }
            }
        }

        // OpenRouter usage: prompt_tokens, completion_tokens, cost, prompt_tokens_details.cached_tokens, provider, model; a
        // "writer" object when the call fell back (each failed attempt keeps its own usage and cost).
        Entry Parse(string task, string? usage, long latency, double? sim, string? error)
        {
            var root = usage is null ? null : JsonNode.Parse(usage) as JsonObject;
            long Long(JsonNode? node) => node is JsonValue v && v.TryGetValue<long>(out var l) ? l : node is JsonValue d && d.TryGetValue<double>(out var x) ? (long)x : 0;
            double Cost(JsonNode? node) => node is JsonValue v && v.TryGetValue<double>(out var x) ? x : 0;
            var writer = root?["writer"]?.DeepClone();
            var failed = (writer?["failures"] as JsonArray ?? []).Sum(f => Cost(f?["usage"]?["cost"]));
            var gemini = (string?)writer?["provider"] == "vertex";
            // Gemini (Agent Framework UsageDetails): InputTokenCount, OutputTokenCount, CachedInputTokenCount.
            var input = Long(root?["prompt_tokens"]) + Long(root?["InputTokenCount"]);
            var output = Long(root?["completion_tokens"]) + Long(root?["OutputTokenCount"]);
            var cached = Long(root?["prompt_tokens_details"]?["cached_tokens"]) + Long(root?["CachedInputTokenCount"]);
            // Gemini Flash-Lite list price (USD per million: 0.30 input, 0.03 cached, 2.50 output), as the POC used.
            var cost = gemini ? ((input - cached) * 0.30 + cached * 0.03 + output * 2.50) / 1e6 : Cost(root?["cost"]);
            return new(phase, task, DateTime.UtcNow, sim, latency, (string?)root?["model"] ?? (string?)writer?["model"], (string?)root?["provider"] ?? (string?)writer?["provider"],
                input, cached, output, cost + failed, gemini, writer, error);
        }
    }

    sealed class BudgetExhausted(string message) : Exception(message);
}
