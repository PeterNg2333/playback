using Playback.Api;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Playback.Api.Activity;
using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Api.Providers;
using System.Globalization;

// Which model should write lecture notes. The Week 3 transcript's first 20 minutes (the span the sample audio
// covers) reach the production NoteWorkflow two minutes at a time, as live notes would see them, once per
// candidate, with an in-memory store, Traditional Chinese output and no Jev gate. Offline it runs the literal
// fixture writer to prove the harness; live needs PLAYBACK_NOTE_MODELS_LIVE=yes and makes at most one call per
// step and candidate. Evidence goes into the run folder; the notes themselves still need a human read.
static class NoteModelComparison
{
    const string SessionId = "week3-note-models";
    const long StepMs = 2 * 60_000;
    const int MaxConsecutiveFailures = 3;
    public static readonly string[] LiveCandidates =
        ["vertex:gemini-3.5-flash-lite", "openrouter:deepseek/deepseek-v4.1-flash", "openrouter:z-ai/glm-5.3-flash"];
    // Vertex list price per million tokens for gemini-3.5-flash-lite, checked 2026-10-18; OpenRouter reports its own cost.
    const double VertexInputUsd = 0.30, VertexOutputUsd = 2.50, CachedShare = 0.10;
    // Common characters that exist only in Simplified Chinese; any of them in Traditional notes is a script error.
    const string SimplifiedOnly = "们这来时说为会对过还发经么样实现点问题应该进开关间网络数据传输协议结构计机讲从预师学习业务记认让话语读写头页类级线组织统码处变节视显际";

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static long Span(string minutes, string text)
    {
        var end = Week3Transcript.Passages(text, long.MaxValue).Select(x => x.End).DefaultIfEmpty().Max();
        if (end == 0) throw new InvalidOperationException("Replay has no timestamped passages");
        if (minutes == "full") return end;
        if (!int.TryParse(minutes, out var value) || value is < 1 or > 180)
            throw new InvalidOperationException("Replay minutes must be 1–180 or full");
        return Math.Min(end, value * 60_000L);
    }

    public static async Task Run(string folder, bool live)
    {
        if (live && Environment.GetEnvironmentVariable("PLAYBACK_NOTE_MODELS_LIVE") != "yes")
            throw new InvalidOperationException("Live note model comparison requires PLAYBACK_NOTE_MODELS_LIVE=yes");
        Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", live ? "no" : "yes");
        Environment.SetEnvironmentVariable("PLAYBACK_AUTO_NOTES", "no");
        Directory.CreateDirectory(folder);
        var source = Environment.GetEnvironmentVariable("PLAYBACK_NOTE_MODELS_SOURCE") ?? "week3";
        var inputFile = source switch { "week3" => Week3Transcript.RelativePath,
            "cantonese-fixture" => "app-temp/checks/Fixtures/cantonese-lecture.txt",
            _ => throw new InvalidOperationException("Replay source must be week3 or cantonese-fixture") };
        var text = await File.ReadAllTextAsync(Path.GetFullPath(inputFile));
        var span = Span(Environment.GetEnvironmentVariable("PLAYBACK_NOTE_MODELS_MINUTES") ?? "20", text);
        var script = Environment.GetEnvironmentVariable("PLAYBACK_NOTE_MODELS_SCRIPT") ?? "raw";
        if (script is not ("raw" or "zh-Hant")) throw new InvalidOperationException("Replay script must be raw or zh-Hant");
        var sources = Week3Transcript.Sources(SessionId, text, span);
        // PLAYBACK_NOTE_MODELS narrows or extends a live run, e.g. "vertex:gemini-3.1-flash-lite,openrouter:deepseek/deepseek-v4.1-flash@none".
        var candidates = Environment.GetEnvironmentVariable("PLAYBACK_NOTE_MODELS") is { Length: > 0 } chosen ? chosen.Split(',', StringSplitOptions.TrimEntries)
            : live ? LiveCandidates : ["fixture:literal-text"];
        if (!live && candidates.Any(x => !x.StartsWith("fixture:", StringComparison.Ordinal)))
            throw new InvalidOperationException("Offline runs use the fixture writer only");
        var results = new List<CandidateResult>();
        var budgetText = Environment.GetEnvironmentVariable("PLAYBACK_NOTE_MODELS_MAX_USD");
        var budget = budgetText is null ? double.PositiveInfinity : double.Parse(budgetText, CultureInfo.InvariantCulture);
        if (budget <= 0 || double.IsNaN(budget)) throw new InvalidOperationException("Replay budget must be positive");
        foreach (var candidate in candidates)
        {
            results.Add(await RunCandidate(folder, candidate, sources, span, script, budget - results.Sum(x => x.NewRequestsAccountedUsd)));
            await Save(folder, live, text, sources, results, inputFile, source, span, script);
        }
        foreach (var result in results)
            Console.WriteLine($"{result.Candidate}: {result.Completed}/{result.Calls} calls, {result.Referenced}/{result.Sources} sources cited, " +
                $"median {result.MedianLatencyMs} ms, {result.CostUsd:0.0000} USD");
        Expect.That(live || results.All(x => x.Failed == 0 && x.ReviewRequired == 0 && x.Unreferenced == 0 && x.PlaceholderPoints == 0),
            "Offline fixture comparison left failures, review stops, placeholders or uncited speech");
        if (results.Any(x => x.Completed == 0)) Environment.ExitCode = 2;
    }

    // A candidate is "provider:model" with an optional pacing after "|":
    //   steps      the writer is asked every two minutes ("Revise with AI"), no gate (default)
    //   jev        live scheduling: every 10 s the real Jev gate decides whether the passage is complete; 30 s deadline
    //   batch      live scheduling as by default: no Jev; the 30 s deadline, a speech boundary or Stop writes
    //   fixed-30s  live scheduling where the gate always waits, so only the 30 s deadline writes
    static async Task<CandidateResult> RunCandidate(string folder, string candidate, List<Transcript> sources, long span, string script, double budget)
    {
        var pacing = candidate.Contains('|') ? candidate[(candidate.IndexOf('|') + 1)..] : "steps";
        // "+organize" keeps automatic organization on, as in production.
        var organize = pacing.EndsWith("+organize", StringComparison.Ordinal);
        pacing = organize ? pacing[..^"+organize".Length] : pacing;
        if (pacing is not ("steps" or "jev" or "batch" or "fixed-30s")) throw new InvalidOperationException("Unknown pacing");
        var (inner, owned) = Writer(candidate.Split('|')[0]);
        var writer = new ReplyRecorder(inner, budget, paid: !candidate.StartsWith("fixture:", StringComparison.Ordinal));
        using var disposeModel = owned;
        var store = new MemoryStore();
        store.Add(SessionId);
        store.Languages[SessionId] = "zh-Hant";
        var activity = new AiActivity(store);
        var clock = new TestClock();
        var resumedFrom = Environment.GetEnvironmentVariable("PLAYBACK_NOTE_MODELS_RESUME");
        if (resumedFrom is not null)
        {
            var root = Path.GetFullPath("app-temp/data/validation/runs") + Path.DirectorySeparatorChar;
            var directory = Path.GetFullPath(resumedFrom);
            if (!directory.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Resume evidence must be inside validation runs");
            var priorSlug = string.Concat(candidate.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_'));
            using var prior = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "live-results.json")));
            var metadata = prior.RootElement;
            if (metadata.GetProperty("notePromptVersion").GetString() != NoteInstructions.Version ||
                !metadata.GetProperty("results").EnumerateArray().Any(x => x.GetProperty("candidate").GetString() == candidate) ||
                metadata.GetProperty("input").GetProperty("script").GetString() != script)
                throw new InvalidOperationException("Resume model, prompt or script differs from saved evidence");
            var priorOutput = Path.Combine(directory, priorSlug);
            var oldSources = JsonSerializer.Deserialize<List<Transcript>>(await File.ReadAllTextAsync(Path.Combine(priorOutput, "sources.json")), Json)!;
            if (!oldSources.Select(x => x.Id).SequenceEqual(sources.Select(x => x.Id))) throw new InvalidOperationException("Resume sources differ from saved evidence");
            var note = JsonSerializer.Deserialize<Note>(await File.ReadAllTextAsync(Path.Combine(priorOutput, "note.json")), Json)!;
            store.Notes[SessionId] = note; store.Versions[SessionId] = [note];
            var cited = note.Sections.SelectMany(x => x.Points).SelectMany(x => x.SourceIds).ToHashSet();
            foreach (var source in sources) { source.NoteStatus = cited.Contains(source.Id) ? "completed" : "pending"; store.Transcripts[SessionId].Add(source); }
            foreach (var item in JsonSerializer.Deserialize<List<ActivityRecord>>(await File.ReadAllTextAsync(Path.Combine(priorOutput, "all-activity.json")), Json)!) store.Activity[item.Id] = item;
            clock.Advance(span / 1000.0);
        }
        var notes = new NoteStoreAdapter(store);
        using var jevTransport = new JevTransport();
        Playback.Application.Notes.INoteGate gate = pacing == "jev" ? new JevNoteGate(jevTransport) : new FixtureNoteGate { Allow = pacing != "fixed-30s" };
        if (pacing != "steps")
        {
            // Organization is off unless asked for, so the counts measure note writing only.
            var settings = await store.SessionProcessingSettings(SessionId);
            await store.SaveSessionProcessingSettings(SessionId, new(settings.Revision,
                new ProcessingOverrides(JevEnabled: pacing is "jev" or "fixed-30s", OrganizationEnabled: organize), settings.InheritedRevision));
        }
        await using var workflow = new NoteWorkflow(notes, notes, writer, gate,
            new NoteActivityAdapter(activity, NullLogger<NoteAgent>.Instance), clock, notes, notes, notes, notes,
            script: script == "zh-Hant" ? new OpenccNoteScript() : null);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(120));
        var steps = new List<object>();
        if (resumedFrom is not null)
        {
            // A continuation writes only uncited sources into the saved document. This is manual recovery,
            // not a claim that the interrupted virtual schedule or its gate checkpoint was restored.
            await workflow.Generate(SessionId, deadline.Token);
            steps.Add(new { resumedFrom, mode = "manual recovery of uncited sources", throughMs = span });
            pacing = "recovery";
        }
        else if (pacing != "steps") await LiveSchedule(store, workflow, clock, sources, candidate, steps, span, deadline.Token);
        var failures = 0;
        for (var through = Math.Min(StepMs, span); pacing == "steps" && through <= span && failures < MaxConsecutiveFailures; through = through == span ? span + 1 : Math.Min(through + StepMs, span))
        {
            var arrived = sources.Where(x => x.EndMs <= through && store.Transcripts[SessionId].All(t => t.Id != x.Id)).ToList();
            store.Transcripts[SessionId].AddRange(arrived.Select(x => new Transcript
            {
                Id = x.Id, SessionId = x.SessionId, SourceId = x.SourceId, StartMs = x.StartMs, EndMs = x.EndMs,
                RecordedAt = x.RecordedAt, Original = x.Original, NoteStatus = "pending"
            }));
            clock.Advance(StepMs / 1000.0);
            var pending = NoteInput.Pending(store.Transcripts[SessionId]).Count;
            if (pending == 0) continue;
            string? error = null;
            try { await workflow.Generate(SessionId, deadline.Token); failures = 0; }
            catch (Exception ex) when (ex is not CheckFailed)
            {
                error = AiActivity.SafeError(ex);
                failures++;
            }
            var call = store.Activity.Values.OrderBy(x => x.StartedAt).LastOrDefault();
            steps.Add(new { throughMs = through, arrived = arrived.Count, pending, status = call?.Status, latencyMs = call?.ProviderLatencyMs, error });
            Console.WriteLine($"{candidate} through {through / 60_000} min: {call?.Status ?? "no call"}{(error is null ? "" : " (" + error + ")")}");
        }

        var slug = string.Concat(candidate.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_'));
        var output = Path.Combine(folder, slug);
        Directory.CreateDirectory(output);
        var session = (await store.Session(SessionId))!;
        var coverage = NoteCoverage.Audit(session);
        var records = store.Activity.Values.Where(x => x.Task == "Note revision").OrderBy(x => x.StartedAt).ToList();
        // Every answered call counts: a rewrite sent to review or a rejected reply took the same provider time.
        var latencies = records.Where(x => x.ProviderLatencyMs is not null).Select(x => x.ProviderLatencyMs!.Value).Order().ToList();
        // Cost includes the organizer's calls; the call counts above are the writer's.
        var usage = records.Concat(store.Activity.Values.Where(x => x.Task == "Section organization")).Select(x => Usage(x.UsageJson)).ToList();
        var markdown = session.NoteMarkdown ?? "";
        var points = session.CurrentNote?.Sections.SelectMany(x => x.Points).ToList() ?? [];
        // A point whose text is only a citation or a few characters ("... [cite_x]") still counts as coverage
        // in the audit, so it is reported separately.
        var placeholders = points.Where(x => Regex.Replace(x.Text, @"\[cite_[0-9a-f]+\]", "").Trim().Length < 12).ToList();
        await File.WriteAllTextAsync(Path.Combine(output, "notes.md"), markdown);
        await File.WriteAllTextAsync(Path.Combine(output, "note.json"), JsonSerializer.Serialize(session.CurrentNote, Json));
        await File.WriteAllTextAsync(Path.Combine(output, "activity.json"), JsonSerializer.Serialize(records, Json));
        await File.WriteAllTextAsync(Path.Combine(output, "steps.json"), JsonSerializer.Serialize(steps, Json));
        await File.WriteAllTextAsync(Path.Combine(output, "all-activity.json"), JsonSerializer.Serialize(store.Activity.Values.OrderBy(x => x.StartedAt), Json));
        await File.WriteAllTextAsync(Path.Combine(output, "suggestions.json"), JsonSerializer.Serialize(store.RewriteProposals.Values, Json));
        await File.WriteAllTextAsync(Path.Combine(output, "replies.json"), JsonSerializer.Serialize(writer.Replies, Json));
        await File.WriteAllTextAsync(Path.Combine(output, "sources.json"), JsonSerializer.Serialize(sources, Json));
        await File.WriteAllTextAsync(Path.Combine(output, "calls.json"), JsonSerializer.Serialize(store.Activity.Values
            .Where(x => x.Task is "Note revision" or "Section organization").OrderBy(x => x.StartedAt).Select(x => {
                var u = Usage(x.UsageJson);
                return new { x.Id, x.Task, x.Status, x.ProviderLatencyMs, inputTokens = u.Input, cachedTokens = u.Cached,
                    cacheHitRatio = u.Input == 0 ? 0 : (double)u.Cached / u.Input, outputTokens = u.Output, cost = u.ReportedCost,
                    x.UsageJson, x.Summary };
            }), Json));
        return new CandidateResult(candidate, writer.Provider, writer.Model,
            Calls: records.Count, Completed: records.Count(x => x.Status == "completed"), ReviewRequired: records.Count(x => x.Status == "review-required"),
            Failed: records.Count(x => x.Status == "failed"),
            Errors: records.Where(x => x.Status == "failed").Select(x => x.Summary ?? "").Distinct().ToList(),
            MedianLatencyMs: latencies.Count == 0 ? null : latencies[latencies.Count / 2], MaxLatencyMs: latencies.Count == 0 ? null : latencies[^1],
            InputTokens: usage.Sum(x => x.Input), CachedInputTokens: usage.Sum(x => x.Cached), OutputTokens: usage.Sum(x => x.Output),
            CostUsd: usage.Sum(x => x.ReportedCost ?? ((x.Input - x.Cached) * VertexInputUsd + x.Cached * VertexInputUsd * CachedShare + x.Output * VertexOutputUsd) / 1_000_000),
            CostSource: writer.Provider == "openrouter" ? "OpenRouter reported usage.cost" : "Vertex list price x reported tokens, cached input at 10% (estimate)",
            NewRequestsAccountedUsd: writer.AccountedUsd,
            Pacing: organize ? pacing + "+organize" : pacing, OrganizerCalls: store.Activity.Values.Count(x => x.Task == "Section organization"), JevCalls: store.Activity.Values.Count(x => x.Task == "Jev note gate"),
            JevAllows: store.Activity.Values.Count(x => x.Task == "Jev note gate" && (x.Summary ?? "").StartsWith("allow")),
            LocalAdmissions: store.Activity.Values.Where(x => x.Task == "Note admission").GroupBy(x => (x.Summary ?? "").Split(':')[0]).ToDictionary(x => x.Key, x => x.Count()),
            ServingProviders: usage.Select(x => x.Provider).OfType<string>().Distinct().ToList(),
            Sources: coverage.Total, Referenced: coverage.Referenced, Unreferenced: coverage.Unreferenced,
            Statuses: store.Transcripts[SessionId].GroupBy(x => x.NoteStatus).ToDictionary(x => x.Key, x => x.Count()),
            AutoEdits: records.Sum(x => int.TryParse(Regex.Match(x.Summary ?? "", @"(\d+) edits applied automatically").Groups[1].Value, out var n) ? n : 0),
            Suggestions: store.RewriteProposals.Count,
            Topics: session.CurrentNote?.Sections.Count ?? 0, EmptyTopics: session.CurrentNote?.Sections.Count(x => x.Points.Count == 0) ?? 0,
            Points: points.Count, PlaceholderPoints: placeholders.Count, SourcesOnlyInPlaceholders: placeholders.SelectMany(x => x.SourceIds)
                .Except(points.Except(placeholders).SelectMany(x => x.SourceIds)).Count(),
            NoteCharacters: markdown.Length, HanCharacters: markdown.Count(IsHan), SimplifiedCharacters: markdown.Count(SimplifiedOnly.Contains));
    }

    // Speech arrives when it was spoken and the production scheduler decides when to write, ticking every
    // 10 s like NoteAgent. Recording stops at the end of the span (Stop flushes what is left).
    static async Task LiveSchedule(MemoryStore store, NoteWorkflow workflow, TestClock clock, List<Transcript> sources, string candidate,
        List<object> steps, long span, CancellationToken ct)
    {
        const long tickMs = 10_000, drainMs = 90_000;
        var stopped = false;
        var consecutiveFailures = 0;
        for (var now = tickMs; now <= span + drainMs; now += tickMs)
        {
            var arrived = sources.Where(x => x.EndMs <= now && store.Transcripts[SessionId].All(t => t.Id != x.Id)).ToList();
            store.Transcripts[SessionId].AddRange(arrived.Select(x => new Transcript
            {
                Id = x.Id, SessionId = x.SessionId, SourceId = x.SourceId, StartMs = x.StartMs, EndMs = x.EndMs,
                RecordedAt = x.RecordedAt, Original = x.Original, NoteStatus = "pending", ConfirmedAt = clock.GetUtcNow().UtcDateTime
            }));
            if (now >= span && !stopped && store.Gates.TryGetValue(SessionId, out var gate))
            {
                gate.FlushRequested = true; gate.FlushVersion++; stopped = true;
            }
            var before = store.Activity.Count;
            clock.Advance(tickMs / 1000.0);
            workflow.Tick([SessionId], ct);
            await workflow.Drain();
            var added = store.Activity.Values.OrderBy(x => x.StartedAt).Skip(before).ToList();
            foreach (var call in added.Where(x => x.Task == "Note revision"))
                consecutiveFailures = call.Status == "failed" ? consecutiveFailures + 1 : 0;
            var tasks = added.Select(x => $"{x.Task}: {x.Status}").ToList();
            if (tasks.Count > 0) steps.Add(new { atMs = now, arrived = arrived.Count, tasks });
            if (consecutiveFailures >= MaxConsecutiveFailures) { Console.WriteLine("Stopped after three consecutive writer failures"); break; }
            if (store.Activity.Values.Any(x => x.Status == "failed" && (x.Summary ?? "").Contains("Replay budget")))
            { Console.WriteLine("Stopped at the replay budget boundary; no further provider call"); break; }
            if (now % 60_000 == 0) Console.WriteLine($"{candidate} at {now / 60_000} min: {store.Activity.Values.Count(x => x.Task == "Note revision")} writer calls, " +
                $"{store.Activity.Values.Count(x => x.Task == "Jev note gate")} Jev calls");
        }
    }

    static (Playback.Application.Notes.INoteWriter Writer, IDisposable? Owned) Writer(string candidate)
    {
        var (provider, model) = (candidate[..candidate.IndexOf(':')], candidate[(candidate.IndexOf(':') + 1)..]);
        switch (provider)
        {
            case "fixture":
                return (new GeminiNoteWriter(new FixtureNoteModel { PlacementReply = true }), null);
            case "vertex":
                // GeminiLanguageModel reads its allowlisted model from the environment on every call.
                Environment.SetEnvironmentVariable("PLAYBACK_GEMINI_MODEL", model);
                var gemini = new GeminiLanguageModel();
                if (!gemini.IsConfigured) throw new InvalidOperationException("Vertex key unavailable in the process environment");
                return (new GeminiNoteWriter(gemini), null);
            case "openrouter":
                // An "@effort" suffix sets the reasoning effort; the default is low.
                var effort = model.Contains('@') ? model[(model.IndexOf('@') + 1)..] : "low";
                var openRouter = RateLimitGuard.Enabled ? new OpenRouterTextModel(new RateLimitGuard(), model.Split('@')[0], effort)
                    : new OpenRouterTextModel(model.Split('@')[0], effort);
                if (!openRouter.IsConfigured) throw new InvalidOperationException("OpenRouter key unavailable in the process environment");
                return (new OpenRouterNoteWriter(openRouter), openRouter);
            default:
                throw new InvalidOperationException("Unknown note model candidate");
        }
    }

    // Vertex usage is Microsoft.Extensions.AI UsageDetails, where TotalTokenCount includes thinking tokens;
    // OpenRouter usage counts reasoning inside completion_tokens and reports cost and the serving provider.
    static (long Input, long Cached, long Output, double? ReportedCost, string? Provider) Usage(string? json)
    {
        if (json is null) return (0, 0, 0, null, null);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        long Read(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0;
        if (root.TryGetProperty("prompt_tokens", out _))
            return (Read("prompt_tokens"), root.TryGetProperty("prompt_tokens_details", out var details) &&
                    details.TryGetProperty("cached_tokens", out var cached) && cached.ValueKind == JsonValueKind.Number ? cached.GetInt64() : 0,
                Read("completion_tokens"),
                root.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Number ? cost.GetDouble() : null,
                root.TryGetProperty("provider", out var provider) ? provider.GetString() : null);
        var input = Read("InputTokenCount");
        var total = Read("TotalTokenCount");
        return (input, Read("CachedInputTokenCount"), total > input ? total - input : Read("OutputTokenCount") + Read("ReasoningTokenCount"), null, "Vertex");
    }

    static bool IsHan(char c) => c is >= '一' and <= '鿿';

    static async Task Save(string folder, bool live, string text, List<Transcript> sources, List<CandidateResult> results,
        string inputFile, string source, long span, string script)
    {
        await File.WriteAllTextAsync(Path.Combine(folder, live ? "live-results.json" : "offline-results.json"), JsonSerializer.Serialize(new
        {
            testedAt = DateTime.UtcNow,
            build = typeof(NoteWorkflow).Assembly.ManifestModule.ModuleVersionId,
            evidence = live ? "Real provider calls through production NoteWorkflow with an in-memory store; no Jev gate, MongoDB, audio upload or ASR"
                : "Offline literal fixture writer; proves the harness, not natural note quality",
            input = new { file = inputFile, sha256 = ContentHash.Of(text), throughMs = span, stepMs = StepMs,
                sources = sources.Count, outputLanguage = "zh-Hant", lectureLanguage = source == "week3" ? "English" : "synthetic Cantonese/English", script,
                resumedFrom = Environment.GetEnvironmentVariable("PLAYBACK_NOTE_MODELS_RESUME") },
            acceptancePassed = results.All(x => x.Failed == 0 && x.ReviewRequired == 0 && x.Sources == sources.Count && x.Unreferenced == 0),
            notePromptVersion = NoteInstructions.Version,
            results
        }, Json));
        var lines = new List<string>
        {
            $"# Note model comparison ({(live ? "live" : "offline fixture")})", "",
            $"{source}, {span / 60_000.0:0.##} minutes, {sources.Count} sources, script {script}, prompt {NoteInstructions.Version}. Raw replies are in replies.json; saved output is in notes.md/note.json.", "",
            "| Candidate | Writer calls completed / review / failed / total | Jev calls (allow) | Local admissions | Cited sources (only by placeholders) | Automatic edits / suggestions | Median / max latency | Tokens in (cached) / out | Cost (USD) | Topics (empty) / points (placeholders) | Simplified chars |",
            "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |"
        };
        if (Environment.GetEnvironmentVariable("PLAYBACK_NOTE_MODELS_RESUME") is { } resume)
            lines.Insert(4, $"Recovered uncited sources from {resume} with a manual generation; this did not restore the interrupted scheduler checkpoint.");
        lines.AddRange(results.Select(r =>
            $"| {r.Candidate.Replace("|", " @ ")} | {r.Completed} / {r.ReviewRequired} / {r.Failed} / {r.Calls} | {r.JevCalls} ({r.JevAllows}) | {string.Join(", ", r.LocalAdmissions.Select(x => x.Key + " " + x.Value))} | {r.Referenced}/{r.Sources} ({r.SourcesOnlyInPlaceholders}) | {r.AutoEdits} / {r.Suggestions} | {r.MedianLatencyMs} / {r.MaxLatencyMs} ms | {r.InputTokens} ({r.CachedInputTokens}) / {r.OutputTokens} | {r.CostUsd:0.0000} | {r.Topics} ({r.EmptyTopics}) / {r.Points} ({r.PlaceholderPoints}) | {r.SimplifiedCharacters} |"));
        lines.Add("");
        lines.AddRange(results.Where(r => r.Errors.Count > 0).Select(r => $"- {r.Candidate} errors: {string.Join("; ", r.Errors)}"));
        await File.WriteAllLinesAsync(Path.Combine(folder, live ? "live-report.md" : "offline-report.md"), lines);
    }

    // Keeps each raw reply (the last streamed text) so refused operations can be inspected after a run.
    sealed class ReplyRecorder(Playback.Application.Notes.INoteWriter inner, double budget, bool paid) : Playback.Application.Notes.INoteWriter
    {
        public readonly List<string> Replies = [];
        double spent;
        public double AccountedUsd => spent;
        public bool IsConfigured => inner.IsConfigured;
        public string Provider => inner.Provider;
        public string Model => inner.Model;
        public async Task<NotePatch> Generate(string name, string instructions, string input, CancellationToken ct, Action<string>? onUpdate = null, Action<string>? onUsage = null)
        {
            var last = "";
            // Conservative token bound: one token per UTF-8 byte, including the generated schema and envelope.
            // Rates per million bound the GLM endpoints checked 2026-10-20 (0.225/0.75), Qwen (0.15/0.47,
            // cache write 0.20) and the configured Vertex price. Recheck prices before future paid runs.
            // Reasoning uses the OpenRouter cap's extra half. Failed requests reserve their full bound.
            var (inputRate, outputRate) = inner.Provider == "vertex" ? (VertexInputUsd, VertexOutputUsd)
                : inner.Model.StartsWith("qwen/", StringComparison.Ordinal) ? (0.20, 0.47) : (0.225, 0.75);
            var schema = ModelOutputContract.ResponseFormat(name, input) is Microsoft.Extensions.AI.ChatResponseFormatJson { Schema: { } value }
                ? value.GetRawText() : "";
            var maximum = (System.Text.Encoding.UTF8.GetByteCount(instructions + input + schema) + 2048) * inputRate / 1_000_000
                + ModelOutputContract.OutputLimit(name) * (inner.Provider == "openrouter" && !inner.Model.StartsWith("qwen/", StringComparison.Ordinal) ? 1.5 : 1) * outputRate / 1_000_000;
            if (paid && spent + maximum > budget) throw new InvalidOperationException("Replay budget cannot cover another bounded request");
            double? charged = null;
            try { return await inner.Generate(name, instructions, input, ct, text => { last = text; onUpdate?.Invoke(text); }, usage => {
                onUsage?.Invoke(usage);
                var u = Usage(usage);
                if (u.ReportedCost is { } cost) charged = cost;
                else if (inner.Provider == "vertex" && u.Input + u.Output > 0)
                    charged = ((u.Input - u.Cached) * VertexInputUsd + u.Cached * VertexInputUsd * CachedShare + u.Output * VertexOutputUsd) / 1_000_000;
                Console.WriteLine($"Usage: {u.Input} in ({u.Cached} cached), {u.Output} out, reported USD {u.ReportedCost?.ToString("0.000000", CultureInfo.InvariantCulture) ?? "not supplied"}");
            }); }
            finally { Replies.Add(last); spent += !paid ? 0 : charged ?? maximum;
                Console.WriteLine($"Replay accounted cost USD {spent.ToString("0.000000", CultureInfo.InvariantCulture)} (failed calls without usage reserve their bound)"); }
        }
    }

    sealed record CandidateResult(string Candidate, string Provider, string Model, int Calls, int Completed, int ReviewRequired, int Failed, List<string> Errors,
        int AutoEdits, int Suggestions, long? MedianLatencyMs, long? MaxLatencyMs, long InputTokens, long CachedInputTokens, long OutputTokens, double CostUsd, string CostSource, double NewRequestsAccountedUsd,
        string Pacing, int OrganizerCalls, int JevCalls, int JevAllows, Dictionary<string, int> LocalAdmissions,
        List<string> ServingProviders, int Sources, int Referenced, int Unreferenced, Dictionary<string, int> Statuses,
        int Topics, int EmptyTopics, int Points, int PlaceholderPoints, int SourcesOnlyInPlaceholders, int NoteCharacters, int HanCharacters, int SimplifiedCharacters);
}
