using Playback.Api;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Playback.Api.Db;
using Playback.Api.Activity;
using Playback.Api.Notes;
using Playback.Api.Ask;
using Playback.Api.Terms;
using Playback.Api.Providers;

// Paid validation of the Week 3 first hour as text: the real NoteAgent, Jev gate and Gemini over an in-memory
// store, then one cited answer and one term explanation. Needs PLAYBACK_WEEK3_LIVE=yes; writes into its run folder.
// API, MongoDB and browser evidence come from separate runs.
static class Week3ProviderRun
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task Run(string folder) {
        if (Environment.GetEnvironmentVariable("PLAYBACK_WEEK3_LIVE") != "yes")
            throw new InvalidOperationException("Week 3 provider checks require explicit PLAYBACK_WEEK3_LIVE=yes");
        Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "no");
        Environment.SetEnvironmentVariable("PLAYBACK_AUTO_NOTES", "no");
        Environment.SetEnvironmentVariable("PLAYBACK_AUTO_TERMS", "no");
        Environment.SetEnvironmentVariable("PLAYBACK_AUTO_ORGANIZE", "no");
        Directory.CreateDirectory(folder);
        var file = Path.GetFullPath("app-temp/data/test-audio/sampleAudio/transcript.txt");
        var text = await File.ReadAllTextAsync(file); var sourceHash = ContentHash.Of(text);
        var store = new MemoryStore(); const string id = "week3-natural-provider"; store.Add(id);
        var paragraphs = new List<(long Start, long End, string Text)>(); long start = 0; var lines = new List<string>();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n')) {
            if (Regex.IsMatch(line.Trim(), @"^\d{1,3}:\d{2}(?::\d{2})?$")) {
                var end = line.Trim().Split(':').Aggregate(0L, (value, part) => value * 60 + long.Parse(part)) * 1000;
                if (lines.Count > 0 && start < 3600000 && end > start) paragraphs.Add((start, Math.Min(end, 3600000), string.Join(' ', lines).Trim()));
                start = end; lines.Clear();
            } else if (line.Trim().Length > 0) lines.Add(line.Trim());
        }
        var epoch = DateTime.UtcNow.Date;
        // Pair neighboring timed text passages for a bounded text-import fixture. Original
        // paragraphs and their exact times remain in provenance; this is not fresh audio/ASR.
        var groups = paragraphs.Chunk(2).Select(parts => (Start: parts[0].Start, End: parts[^1].End, Text: string.Join(" ", parts.Select(x => x.Text)))).ToList();
        foreach (var p in groups) store.Transcripts[id].Add(new Transcript {
            Id = "week3_" + ContentHash.Of($"{sourceHash}:{p.Start}:{p.Text}")[..24], SessionId = id, SourceId = "week3-text",
            StartMs = p.Start, EndMs = p.End, RecordedAt = epoch.AddMilliseconds(p.Start), Original = p.Text, NoteStatus = "pending"
        });
        await File.WriteAllTextAsync(Path.Combine(folder, "input-provenance.json"), JsonSerializer.Serialize(new {
            file, sourceHash, rawPassages = paragraphs.Select(p => new { startMs = p.Start, endMs = p.End, original = p.Text }),
            textImportSources = store.Transcripts[id], interpretation = "Exact repository transcript text paired for provider-input validation, not new audio recognition or semantic quality proof."
        }, JsonOptions));
        var maxBatches = int.TryParse(Environment.GetEnvironmentVariable("PLAYBACK_WEEK3_NOTE_BATCHES"), out var requestedBatches)
            ? Math.Clamp(requestedBatches, 1, 9) : 9;
        var clock = new TestClock(); var activity = new AiActivity(store); var model = new GeminiLanguageModel();
        using var transport = new JevTransport(); var gate = new JevNoteGate(transport);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(18));
        var ct = deadline.Token; var errors = new List<object>(); var direct = new List<object>(); var versions = new List<Note>();
        await using var agent = new NoteAgent(store, model, gate, NullLogger<NoteAgent>.Instance, activity, clock);
        async Task Save() {
            var records = store.Activity.Values.OrderBy(x => x.StartedAt).ToList();
            await File.WriteAllTextAsync(Path.Combine(folder, "provider-results.json"), JsonSerializer.Serialize(new {
                testedAt = DateTime.UtcNow, evidence = "Live Jev/Gemini through production NoteAgent; in-memory persistence, no audio upload",
                source = new { file, sha256 = sourceHash, fromMs = 0, throughMs = 3600000, passages = paragraphs.Count, textImportSources = groups.Count },
                model = model.Model, notePromptVersion = NoteInstructions.Version, organizePromptVersion = NoteInstructions.OrganizeVersion,
                gatePromptVersion = JevNoteGate.PromptVersion, maxBatches, records, direct, errors,
                noteVersions = versions.Select(n => new { n.Version, sectionCount = n.Sections.Count, pointCount = n.Sections.Sum(s => s.Points.Count), markdownHash = ContentHash.Of(n.Markdown) }),
                statuses = store.Transcripts[id].GroupBy(x => x.NoteStatus).ToDictionary(x => x.Key, x => x.Count()),
                cost = new { type = "usage-based estimate; not an invoice", pricingChecked = "2026-09-29",
                    vertexGlobalUsdPerMillionInput = .25, vertexGlobalUsdPerMillionOutput = 1.50, jev113UsdPerMillionInput = .042,
                    pricingSources = new[] { "https://cloud.google.com/vertex-ai/generative-ai/pricing", "https://docs.typesafe.ai/models" },
                    exclusions = "Account credits/free tier, non-global surcharge, unknown failed-call usage and Google Search queries. No ASR calls in this run." }
            }, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }));
            if (store.Notes.TryGetValue(id, out var latest)) {
                await File.WriteAllTextAsync(Path.Combine(folder, "generated-notes.md"), latest.Markdown);
                await File.WriteAllTextAsync(Path.Combine(folder, "session.json"), JsonSerializer.Serialize(await store.Session(id), JsonOptions));
            }
        }
        async Task Attempt(string name, Func<Task> work) {
            try { await work(); Console.WriteLine($"Week 3 {name}: completed"); }
            catch (Exception ex) { errors.Add(new { task = name, error = AiActivity.SafeError(ex) }); Console.WriteLine($"Week 3 {name}: failed; inspect saved safe error"); }
            await Save();
        }
        if (!model.IsConfigured || !gate.IsConfigured) {
            errors.Add(new { task = "configuration", error = "Jev/Gemini credentials unavailable in process environment" }); await Save(); Environment.ExitCode = 2; return;
        }
        await Attempt("gate-complete", async () => {
            var passage = paragraphs.Where(x => x.Start >= 1200000 && x.Start < 1500000).ToList();
            var state = JsonSerializer.Serialize(new { pending = passage.Select(x => new { startMs = x.Start, endMs = x.End, text = x.Text }), trigger = "automatic" }, JsonOptions);
            var result = await gate.Decide(state, ct); direct.Add(new { task = "gate-complete", inputHash = ContentHash.Of(state), result });
        });
        await Attempt("gate-fragment", async () => {
            const string state = "{\"pending\":[{\"text\":\"The formula we are about to derive is\"}],\"trigger\":\"automatic\"}";
            var result = await gate.Decide(state, ct); direct.Add(new { task = "gate-fragment", inputHash = ContentHash.Of(state), result });
        });
        for (var batch = 1; batch <= maxBatches && NoteInput.Pending(store.Transcripts[id]).Count > 0; batch++) {
            var before = store.Activity.Count;
            await Attempt("notes-batch-" + batch, async () => {
                clock.Advance(70); agent.Tick([id], ct); await agent.Drain();
                if (store.Notes.TryGetValue(id, out var note) && versions.All(n => n.Version != note.Version)) {
                    versions.Add(JsonSerializer.Deserialize<Note>(JsonSerializer.Serialize(note))!);
                    await File.WriteAllTextAsync(Path.Combine(folder, $"note-v{note.Version}.json"), JsonSerializer.Serialize(note, JsonOptions));
                }
                var failed = store.Activity.Values.Where(x => x.Status == "failed").OrderByDescending(x => x.StartedAt).FirstOrDefault();
                if (failed is not null && store.Activity.Count > before) throw new InvalidOperationException(failed.Summary);
            });
            if (store.Activity.Count == before) break;
            if (store.Gates.TryGetValue(id, out var savedGate) && savedGate.Status == "wait") { savedGate.FlushRequested = true; savedGate.FlushVersion++; }
            if (errors.Count > 2) break;
        }
        if (store.Notes.TryGetValue(id, out var latestNote) && latestNote.Sections.Count > 0) await Attempt("organize", async () => {
            var before = latestNote.Sections.ToDictionary(x => x.Id, x => x.Markdown); var sectionId = latestNote.Sections[0].Id;
            await agent.Organize(id, sectionId, latestNote.Version, ct);
            var after = store.Notes[id]; Expect.That(after.Sections.Where(x => x.Id != sectionId).All(x => before[x.Id] == x.Markdown), "Organizer modified another section");
            versions.Add(JsonSerializer.Deserialize<Note>(JsonSerializer.Serialize(after))!);
            await File.WriteAllTextAsync(Path.Combine(folder, "organized-note.json"), JsonSerializer.Serialize(after, JsonOptions));
        });
        var session = (await store.Session(id))!;
        await Attempt("source-backed-answer", async () => {
            var input = new QuestionInput("Explain the main technical concepts in this lecture. State which statements the sources actually support.");
            var context = ChatContextBuilder.Build(session, input); string? usage = null;
            var watch = System.Diagnostics.Stopwatch.StartNew(); var status = "failed"; string? answer = null;
            SessionEvidence[] evidence = [];
            try {
                answer = context.References!.Decode(await model.Generate("PlaybackQuestionAnswerer", ChatAgent.Instructions, context.Prompt, ct, onUsage: value => usage = value));
                evidence = ChatAgent.CitedEvidence(context, answer); status = "completed";
            } finally { direct.Add(new { task = "source-backed-answer", status, promptVersion = ChatAgent.PromptVersion, inputHash = ContentHash.Of(context.Prompt),
                usageJson = usage, providerLatencyMs = watch.ElapsedMilliseconds, answer, evidence }); }
        });
        var term = TermCandidateExtractor.Find([], store.Transcripts[id]).FirstOrDefault();
        if (term is not null) await Attempt("term-detail", async () => {
            var rank = await new JevTermClassifier(transport).Rank(term.Text, ct, term.Context);
            direct.Add(new { task = "term-rank", term = term.Text, rank });
            string? usage = null; GroundedResult? result = null; var watch = System.Diagnostics.Stopwatch.StartNew();
            try { result = await model.GroundedExplain($"Term: {term.Text}\nContext: {term.Context}\nOutput language: Traditional Chinese. Explain lecture use separately from sourced web supplement.", ct,
                value => usage = value); }
            finally { direct.Add(new { task = "term-detail", term = term.Text, status = result is null ? "failed" : "completed",
                promptVersion = GeminiLanguageModel.ExplanationPromptVersion, answer = result?.Answer, usageJson = usage, evidence = result?.Evidence,
                providerLatencyMs = watch.ElapsedMilliseconds }); }
        });
        var unprocessed = store.Transcripts[id].Count(x => x.NoteStatus is not ("completed" or "suppressed" or "deferred"));
        if (unprocessed > 0) errors.Add(new { task = "input-coverage", error = $"{unprocessed} imported sources remain unprocessed at the bounded run limit; no full-hour quality claim." });
        await Save();
        Console.WriteLine($"Week 3 provider validation saved: {paragraphs.Count} natural passages, {versions.Count} note versions, {errors.Count} failures. No Mongo/browser/hardware claims.");
        if (errors.Count > 0) Environment.ExitCode = 1;
    }
}
