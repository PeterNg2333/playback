using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

using Playback.Domain;
using Playback.Domain.Notes;

namespace Playback.Application.Notes;

// Writes a session's notes with Gemini: new confirmed speech, Revise with AI, one organized section or
// a coverage-gap repair. Every run saves one section patch as a new note version. When automatic runs
// happen is decided in NoteWorkflow.Automatic.cs.
public sealed partial class NoteWorkflow : IAsyncDisposable
{
    const int MaxConcurrentGenerations = 4;

    readonly ConcurrentDictionary<string, SemaphoreSlim> sessionLocks = new();
    readonly SemaphoreSlim generationSlots = new(MaxConcurrentGenerations, MaxConcurrentGenerations);
    readonly INoteRepository repository;
    readonly INoteEvidence evidence;
    readonly INoteWriter gemini;
    readonly INoteGate jev;
    readonly INoteActivity activity;
    readonly TimeProvider clock;
    readonly NoteScheduler scheduler;
    readonly INoteProcessingPolicy? processingPolicy;
    readonly DateTime startedAt;

    public NoteWorkflow(INoteRepository repository, INoteEvidence evidence, INoteWriter writer, INoteGate gate,
        INoteActivity activity, TimeProvider? clock = null, INoteProcessingPolicy? processingPolicy = null)
    {
        this.repository = repository;
        this.evidence = evidence;
        gemini = writer;
        jev = gate;
        this.activity = activity;
        this.clock = clock ?? TimeProvider.System;
        this.processingPolicy = processingPolicy;
        startedAt = this.clock.GetUtcNow().UtcDateTime;
        scheduler = new NoteScheduler(this.clock, Automatic, interval: TimeSpan.FromSeconds(2));
    }

    public bool Due => scheduler.Due;

    // Revise with AI: pending speech and new materials first; with none, every editable section.
    public async Task<object> Generate(string id, CancellationToken ct)
    {
        var sessionLock = SessionLock(id);
        await sessionLock.WaitAsync(ct);
        try
        {
            var session = await evidence.Session(id) ?? throw new InvalidOperationException("Session not found");
            var pending = NoteInput.Pending(session);
            var sections = session.Sections;
            if (pending.Count > 0 || NoteInput.Materials(session.Materials, session.CurrentNote).Count > 0)
                return await Revise(session, pending, NoteInput.ContextSections(sections, pending), ct);
            object? result = null;
            // Explicit revision visits every editable section with its own sources, not the last 40 chunks.
            foreach (var section in sections.Where(x => !x.UserEdited))
            {
                session = await evidence.Session(id) ?? throw new InvalidOperationException("Session not found");
                var current = session.Sections.Single(x => x.Id == section.Id);
                result = await Revise(session, [], [current], ct);
            }
            return result ?? throw new InvalidOperationException(
                "No editable section; user-edited sections are protected. Select a section to organize it explicitly.");
        }
        finally { sessionLock.Release(); }
    }

    public async Task<object> Organize(string id, string sectionId, int baseVersion, CancellationToken ct)
    {
        var sessionLock = SessionLock(id);
        await sessionLock.WaitAsync(ct);
        try
        {
            var session = await evidence.Session(id) ?? throw new InvalidOperationException("Session not found");
            if (session.NoteVersion != baseVersion) throw new InvalidOperationException("Notes changed; reload before organizing");
            var section = session.Sections.SingleOrDefault(x => x.Id == sectionId)
                ?? throw new InvalidOperationException("Section not found");
            return await Revise(session, [], [section], ct, organize: true);
        }
        finally { sessionLock.Release(); }
    }

    // Writes the earliest actionable gap as new content; existing sections and user edits stay.
    public async Task<object> RepairCoverage(string id, int baseVersion, CancellationToken ct)
    {
        var sessionLock = SessionLock(id);
        await sessionLock.WaitAsync(ct);
        try
        {
            var session = await evidence.Session(id) ?? throw new InvalidOperationException("Session not found");
            if (session.NoteVersion != baseVersion) throw new InvalidOperationException("Notes changed; reload the coverage report");
            var pending = NoteCoverage.OldestBatch(session);
            if (pending.Count == 0)
                throw new InvalidOperationException("No unreferenced speech; citations alone do not prove semantic completeness");
            return await Revise(session, pending, [], ct, repair: true);
        }
        finally { sessionLock.Release(); }
    }

    SemaphoreSlim SessionLock(string id) => sessionLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));

    // One model run: build the bounded input, stream the reply, apply it as a section patch and save
    // the new version. Sources the reply neither wrote nor deferred stay pending.
    async Task<object> Revise(NoteSession session, List<NoteTranscript> pending, List<NoteSection> context, CancellationToken ct,
        bool organize = false, bool repair = false, string admissionReason = "manual")
    {
        var materials = organize || repair ? new List<NoteMaterial>() : NoteInput.Materials(session.Materials, session.CurrentNote);
        var instructions = NoteInstructions.For(session.NoteLanguage) + (organize ? "\n" + NoteInstructions.Organize : "");
        var call = await activity.Begin(session.Id, organize ? "Section organization" : "Note revision", "vertex", gemini.Model,
            pending.Select(x => x.Id).Concat(materials.Select(x => x.Id)).Concat(context.SelectMany(x => x.Points).SelectMany(x => x.SourceIds)),
            session.NoteVersion);
        call.SectionId = organize ? context[0].Id : null;
        var admitted = false;
        try
        {
            var input = organize
                ? NoteInput.Build(session, pending, context, materials, "organize")
                : NoteInput.Bounded(session, pending, context, materials, repair ? "gap-repair" : admissionReason);
            var hash = ContentHash.Of(gemini.Model + instructions + input.Text);
            activity.Context(call, organize ? NoteInstructions.OrganizeVersion : NoteInstructions.Version, instructions, input.Text);
            await generationSlots.WaitAsync(ct);
            admitted = true;
            await activity.Start(call);
            var watch = Stopwatch.StartNew();
            NotePatch patch;
            try
            {
                patch = await gemini.Generate(organize ? "LectureSectionOrganizer" : "LectureSectionWriter", instructions, input.Text, ct,
                    onUpdate: text => activity.Draft(call, input.Decode(ReadableDraft(text))), onUsage: usage => call.UsageJson = usage);
            }
            finally { call.ProviderLatencyMs = watch.ElapsedMilliseconds; }

            var allowed = session.AllowedSources.ToList();
            DecodePatch(patch, input, context, session, allowed);
            // Newly compacted legacy citations must be available for decoding before applying the patch.
            var baseSections = session.Sections;
            var baseNote = session.CurrentNote ?? new Note { SessionId = session.Id };
            baseNote.Citations = NoteSections.CloneCitations(baseNote.Citations);
            foreach (var section in context) NoteSections.Compact(session.Id, section.Markdown, allowed, baseNote.Citations);
            var update = NoteSections.Apply(session.Id, baseNote, baseSections, patch, context.Select(x => x.Id),
                pending.Select(x => x.Id).Concat(materials.Select(x => x.Id)), allowed, hash, allowUserEdited: organize);
            if (patch.Sections.Count == 0)
            {
                await evidence.MarkNotes(pending.Select(x => x.Id), "deferred", "Generation deferred this input; it has not been incorporated into notes");
                await activity.End(call, "completed", "No section change; all input deferred, no note version saved");
                return new { Version = session.NoteVersion, Markdown = session.NoteMarkdown, Author = session.CurrentNote?.Author };
            }
            if (organize)
                foreach (var section in update.Sections.Where(x => context.Any(s => s.Id == x.Id)))
                {
                    section.OrganizedAt = DateTime.UtcNow;
                    section.OrganizedVersion = section.Version;
                }
            foreach (var item in update.Coverage)
                item.ContentHash = ContentHash.Of(pending.FirstOrDefault(x => x.Id == item.SourceId)?.SourceText
                    ?? materials.First(x => x.Id == item.SourceId).Text);
            RequireKnownReferences(patch, update, context, session, allowed);

            var result = await repository.Save(session.Id, update, hash, session.NoteVersion, session.NoteLanguage,
                author: organize ? "agent organization" : "agent");
            await MarkSources(update, pending);
            await activity.End(call, "completed",
                $"Section patch saved; untouched sections retained; {update.Coverage.Count(x => x.Status == "covered")} sources have written points, " +
                $"{update.Coverage.Count(x => x.Status == "deferred")} deferred, {update.Coverage.Count(x => x.Status == "pending")} remain pending without a model disposition");
            return result;
        }
        catch (Exception ex) { await activity.Fail(call, ex); throw; }
        finally { if (admitted) generationSlots.Release(); }
    }

    // The reply cites input aliases; store the real source IDs and show every point's sources as a citation.
    static void DecodePatch(NotePatch patch, NotePrompt input, List<NoteSection> context, NoteSession session, List<string> allowed)
    {
        foreach (var section in patch.Sections)
        {
            section.BaseVersion = context.FirstOrDefault(x => x.Id == section.Id)?.Version ?? 0;
            foreach (var point in section.Points)
            {
                point.Text = input.Decode(point.Text);
                point.SourceIds = point.SourceIds.SelectMany(x => input.Aliases.TryGetValue(x, out var source) ? new[] { source } :
                    session.CurrentNote?.Citations.FirstOrDefault(c => c.Id == x)?.SourceIds.ToArray() ?? [x]).Distinct().ToList();
                var cited = NoteSections.Sources(NoteSections.Expand(point.Text, session.CurrentNote?.Citations ?? []), allowed);
                var missing = point.SourceIds.Except(cited).ToArray();
                if (missing.Length > 0) point.Text += " [" + string.Join(", ", missing) + "]";
            }
            section.Markdown = "## " + section.Title + "\n\n" + string.Join("\n\n", section.Points.Select(x => x.Text));
        }
        foreach (var deferred in patch.Deferred)
            if (input.Aliases.TryGetValue(deferred.SourceId, out var source)) deferred.SourceId = source;
    }

    // Older versions can contain unavailable historical IDs. Copying them remains visibly unavailable;
    // no new source ID may be invented and none can be counted as coverage.
    static void RequireKnownReferences(NotePatch patch, SectionUpdate update, List<NoteSection> context, NoteSession session, List<string> allowed)
    {
        foreach (var section in patch.Sections)
        {
            NoteReferences.ValidateReferences(section.Markdown, session.TermReferenceIds);
            var priorReferences = context.SelectMany(s => CitationSyntax.Bodies(s.Markdown))
                .SelectMany(body => body.Split([',', ';'], StringSplitOptions.TrimEntries))
                .Where(x => !x.StartsWith("ref:") && !Regex.IsMatch(x, @"^(?:T\d+|cite_.*)$", RegexOptions.IgnoreCase))
                .ToList();
            NoteReferences.ValidateSourceReferences(NoteSections.Expand(section.Markdown, update.Citations),
                allowed.Concat(priorReferences).Concat(update.Citations.Select(c => c.Id)));
        }
    }

    async Task MarkSources(SectionUpdate update, List<NoteTranscript> pending)
    {
        var pendingIds = pending.Select(x => x.Id);
        await evidence.MarkNotes(update.Coverage.Where(x => x.Status == "covered").Select(x => x.SourceId).Intersect(pendingIds), "completed");
        await evidence.MarkNotes(update.Coverage.Where(x => x.Status == "deferred").Select(x => x.SourceId).Intersect(pendingIds),
            "deferred", "Retained for continuation; the saved source disposition explains why");
        await evidence.MarkNotes(update.Coverage.Where(x => x.Status == "pending").Select(x => x.SourceId).Intersect(pendingIds),
            "pending", "No written point or deferral; still awaiting coverage");
    }

    // The streamed reply is JSON; the activity popover shows the Markdown inside it as it arrives.
    public static string ReadableDraft(string json)
    {
        var sections = new List<string>();
        var field = json.Contains("\"markdown\"", StringComparison.OrdinalIgnoreCase) ? "markdown" : "text";
        foreach (Match match in Regex.Matches(json, "\"" + field + "\"\\s*:\\s*\"(?<body>(?:\\\\.|[^\"\\\\])*)", RegexOptions.IgnoreCase))
        {
            try { sections.Add(JsonSerializer.Deserialize<string>("\"" + match.Groups["body"].Value + "\"") ?? ""); }
            catch (JsonException) { /* an incomplete escape will become readable on the next streaming update */ }
            if (field == "markdown" && sections.Count == 4) break;
        }
        return string.Join("\n\n", sections);
    }

    public async ValueTask DisposeAsync() => await Drain();
}
