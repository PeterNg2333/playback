using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

using Playback.Domain;
using Playback.Domain.Notes;

namespace Playback.Application.Notes;

// Writes a session's notes with the configured note writer (Gemini or an OpenRouter model): new confirmed
// speech, Revise with AI, one organized section or a coverage-gap repair. Every run saves one section patch
// as a new note version. When automatic runs happen is decided in NoteWorkflow.Automatic.cs.
public sealed partial class NoteWorkflow : IAsyncDisposable
{
    const int MaxConcurrentGenerations = 4;

    readonly ConcurrentDictionary<string, SemaphoreSlim> sessionLocks = new();
    readonly SemaphoreSlim generationSlots = new(MaxConcurrentGenerations, MaxConcurrentGenerations);
    readonly SemaphoreSlim organizationSlots = new(1, 1);
    readonly INoteRepository repository;
    readonly INoteEvidence evidence;
    readonly INoteWriter writer;
    readonly INoteGate jev;
    readonly INoteActivity activity;
    readonly TimeProvider clock;
    readonly NoteScheduler scheduler;
    readonly INoteProcessingPolicy? processingPolicy;
    readonly INoteSpeechBoundaries? boundaries;
    readonly INoteOrganizationRepository? organization;
    readonly INoteRewriteRepository? rewrites;
    readonly INoteScript? script;
    readonly DateTime startedAt;

    public NoteWorkflow(INoteRepository repository, INoteEvidence evidence, INoteWriter writer, INoteGate gate,
        INoteActivity activity, TimeProvider? clock = null, INoteProcessingPolicy? processingPolicy = null, INoteSpeechBoundaries? boundaries = null, INoteOrganizationRepository? organization = null, INoteRewriteRepository? rewrites = null,
        INoteScript? script = null)
    {
        this.repository = repository;
        this.evidence = evidence;
        this.writer = writer;
        jev = gate;
        this.activity = activity;
        this.clock = clock ?? TimeProvider.System;
        this.processingPolicy = processingPolicy;
        this.boundaries = boundaries;
        this.organization = organization;
        this.rewrites = rewrites;
        this.script = script;
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
        bool organize = false, bool repair = false, string admissionReason = "manual", bool outlineOnly = false, bool automaticOrganization = false, string? persistenceHash = null)
    {
        var policy = processingPolicy is null ? ProcessingPolicy.Defaults : (await processingPolicy.Effective(session.Id)).Values;
        var materials = organize || repair ? new List<NoteMaterial>() : NoteInput.Materials(session.Materials, session.CurrentNote);
        var instructions = NoteInstructions.For(session.NoteLanguage) + (organize ? "\n" + NoteInstructions.Organize : "");
        var call = await activity.Begin(session.Id, organize ? "Section organization" : "Note revision", writer.Provider, writer.Model,
            pending.Select(x => x.Id).Concat(materials.Select(x => x.Id)).Concat(context.SelectMany(x => x.Points).SelectMany(x => x.SourceIds)),
            session.NoteVersion);
        call.SectionId = organize ? context[0].Id : null;
        var admitted = false;
        try
        {
            var input = outlineOnly ? NoteInput.Outline(session, context) : organize
                ? NoteInput.Build(session, pending, context, materials, "organize")
                : NoteInput.Bounded(session, pending, context, materials, repair ? "gap-repair" : admissionReason);
            var hash = persistenceHash ?? ContentHash.Of(writer.Model + instructions + input.Text);
            activity.Context(call, organize ? NoteInstructions.OrganizeVersion : NoteInstructions.Version, instructions, input.Text);
            await generationSlots.WaitAsync(ct);
            admitted = true;
            await activity.Start(call);
            var watch = Stopwatch.StartNew();
            NotePatch patch;
            try
            {
                patch = await writer.Generate(organize ? "LectureSectionOrganizer" : "LectureSectionWriter", instructions, input.Text, ct,
                    onUpdate: text => activity.Draft(call, input.Decode(ReadableDraft(text))), onUsage: usage => call.UsageJson = usage);
            }
            finally { call.ProviderLatencyMs = watch.ElapsedMilliseconds; }

            var allowed = session.AllowedSources.ToList();
            ExpandPlacements(patch);
            SplitTopicContent(patch);
            InScript(patch, session.NoteLanguage);
            var distantMoves = 0;
            OrganizerDrops? outOfScope = null;
            DecodePatch(patch, input, context, session, allowed);
            if (organize) { outOfScope = DropOutOfScope(patch, context); distantMoves = DropDistantMoves(patch, session); }
            // Organization stays structural and all-or-nothing; writing applies each change on its own.
            var edits = organize ? null : await TriageEdits(session, patch, allowed, pending);
            var proposals = organize ? RewriteCandidates(session, patch) : [];
            if (proposals.Count > 0)
            {
                if (rewrites is null) throw new InvalidOperationException("Body rewrite requires a saved preview; prior content remains unchanged");
                foreach (var proposal in proposals) await rewrites.SaveRewrite(proposal);
                await evidence.MarkNotes(pending.Select(x => x.Id), "deferred", "Rewrite preview requires review; input remains unwritten");
                await activity.End(call, "review-required", "Prior bodies preserved in position; inspect the retained-block rewrite preview before accepting");
                return new NoteRewritePending(session.NoteVersion, session.NoteMarkdown, proposals.Select(x => x.Id).ToList());
            }
            // Newly compacted legacy citations must be available for decoding before applying the patch.
            var baseSections = session.Sections;
            var baseNote = session.CurrentNote ?? new Note { SessionId = session.Id };
            baseNote.Citations = NoteSections.CloneCitations(baseNote.Citations);
            foreach (var section in context) NoteSections.Compact(session.Id, section.Markdown, allowed, baseNote.Citations);
            var update = NoteSections.Apply(session.Id, baseNote, baseSections, patch, context.Select(x => x.Id),
                pending.Select(x => x.Id).Concat(materials.Select(x => x.Id)), allowed, hash, allowUserEdited: organize);
            if (patch.Operations.Count > 0)
            {
                if (outlineOnly && patch.Operations.Any(x => x.Kind is not ("create-topic" or "move-topic" or "reorder-topic" or "rename-topic"))) throw new InvalidOperationException("Outline organization may change only topic structure");
                List<NoteSection> operated;
                List<string> edited = [];
                if (edits is null)
                {
                    // Splitting a large topic moves each block, so organization may use more operations than writing.
                    operated = NoteTree.Apply(update.Sections, patch.Operations, allowed, hash, allowProtected: organize && !automaticOrganization, structuralOnly: organize,
                        maxOperations: organize ? NoteTree.MaxOrganizationOperations : NoteTree.MaxOperations);
                    KeepLectureOrder(operated, update.Sections.Select(x => x.Id).ToHashSet(), session);
                }
                else
                {
                    (operated, var rejected) = NoteTree.ApplyEach(update.Sections, patch.Operations, allowed, hash);
                    edits.Dropped.AddRange(rejected);
                    KeepLectureOrder(operated, update.Sections.Select(x => x.Id).ToHashSet(), session);
                    edited = patch.Operations.Where(x => x.Kind is "replace-text" or "delete-block" && rejected.All(r => r.Operation != x)).Select(x => x.BlockId).ToList();
                }
                var points = operated.SelectMany(x => x.Points).ToList();
                var coverage = pending.Select(x => x.Id).Concat(materials.Select(x => x.Id)).Select(source =>
                {
                    var written = points.Where(x => x.SourceIds.Contains(source)).Select(x => x.Id).ToList();
                    var deferred = patch.Deferred.FirstOrDefault(x => x.SourceId == source);
                    return new SourceDisposition { SourceId = source, PointIds = written, Status = written.Count > 0 ? "covered" : deferred is not null ? "deferred" : "pending",
                        Reason = written.Count > 0 ? "Source-backed canonical block saved" : deferred?.Reason ?? "No block or deferral; source remains pending" };
                }).ToList();
                update = new(operated, update.Citations, coverage, EditedBlockIds: edited);
            }
            if (patch.Sections.Count == 0 && patch.Operations.Count == 0)
            {
                await evidence.MarkNotes(pending.Select(x => x.Id), "deferred", "Generation deferred this input; it has not been incorporated into notes");
                await activity.End(call, "completed", "No section change; all input deferred, no note version saved" + EditSummary(edits) +
                    Dropped(distantMoves, outOfScope));
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
            update = update with { Presentation = policy.NotePresentation };

            var result = await repository.Save(session.Id, update, hash, session.NoteVersion, session.NoteLanguage,
                author: organize ? "agent organization" : "agent");
            await MarkSources(update, pending);
            await activity.End(call, "completed",
                $"Section patch saved; untouched sections retained; {update.Coverage.Count(x => x.Status == "covered")} sources have written points, " +
                $"{update.Coverage.Count(x => x.Status == "deferred")} deferred, {update.Coverage.Count(x => x.Status == "pending")} remain pending without a model disposition" +
                EditSummary(edits) + Dropped(distantMoves, outOfScope));
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
                point.Text = input.Decode(SourceRefsAsCitations(point.Text, input));
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
        foreach (var operation in patch.Operations)
        {
            operation.Body = SourceRefsAsCitations(operation.Body, input);
            operation.Replace = SourceRefsAsCitations(operation.Replace, input);
            operation.TopicId = input.Aliases.GetValueOrDefault(operation.TopicId, operation.TopicId);
            if (operation.ParentId is not null) operation.ParentId = input.Aliases.GetValueOrDefault(operation.ParentId, operation.ParentId);
            operation.Body = input.Decode(operation.Body);
            operation.Replace = input.Decode(operation.Replace);
            operation.SourceIds = operation.SourceIds.Select(x => input.Aliases.GetValueOrDefault(x, x)).ToList();
            if (operation.Kind == "add-block")
            {
                operation.Body = WithoutCopiedCitations(operation.Body);
                var cited = NoteSections.Sources(operation.Body, allowed);
                var missing = operation.SourceIds.Except(cited).ToList();
                if (missing.Count > 0) operation.Body += " [" + string.Join(", ", missing) + "]";
            }
        }
        foreach (var rewrite in patch.Rewrites) rewrite.Body = input.Decode(rewrite.Body);
    }

    // Models write source citations as term references ("[ref:T001, T002]"). When every ID is a source alias in
    // this input it is a source citation; any other [ref:...] stays and is checked as a term reference.
    static string SourceRefsAsCitations(string text, NotePrompt input) => Regex.Replace(text, @"\[ref:\s*([^\]]+)\]", match =>
    {
        var ids = match.Groups[1].Value.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return ids.Length > 0 && ids.All(input.Aliases.ContainsKey) ? "[" + string.Join(", ", ids) + "]" : match.Value;
    });

    // A new block cites through its sourceIds. Models also paste an existing cite_* marker into the text as a
    // generic citation, which attributes the claim to an unrelated earlier source, so those markers are removed.
    static string WithoutCopiedCitations(string body) => Regex.Replace(
        CitationSyntax.Rewrite(body, inner => inner.Split([',', ';'], StringSplitOptions.TrimEntries).All(x => x.StartsWith("cite_", StringComparison.Ordinal)) ? "" : inner),
        @"[ \t]*\[\]", "");

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
        // Operation text was checked one operation at a time in TriageEdits; organization adds no text.
    }

    static string EditSummary(EditOutcome? edits) => edits is null ? "" :
        $"; {edits.Appended} appended to existing topics, {edits.AutoEdited} edits applied automatically, {edits.Suggested} suggestions for review" +
        (edits.RepeatDeferrals == 0 ? "" : $", {edits.RepeatDeferrals} repeated deferrals ignored") +
        (edits.Redirected == 0 ? "" : $", {edits.Redirected} blocks for closed topics placed in the subtopic the reply declared") +
        (edits.ClosedKept == 0 ? "" : $", {edits.ClosedKept} blocks for closed topics kept there without a declared subtopic") +
        (edits.Dropped.Count == 0 ? "" : $", {edits.Dropped.Count} changes not applied ({string.Join("; ", edits.Dropped.Select(x => x.Reason).Distinct().Take(3))})");

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
        var field = json.Contains("\"body\"", StringComparison.OrdinalIgnoreCase) ? "body" : json.Contains("\"markdown\"", StringComparison.OrdinalIgnoreCase) ? "markdown" : "text";
        foreach (Match match in Regex.Matches(json, "\"" + field + "\"\\s*:\\s*\"(?<body>(?:\\\\.|[^\"\\\\])*)", RegexOptions.IgnoreCase))
        {
            try { sections.Add(JsonSerializer.Deserialize<string>("\"" + match.Groups["body"].Value + "\"") ?? ""); }
            catch (JsonException) { /* an incomplete escape will become readable on the next streaming update */ }
            if (field == "markdown" && sections.Count == 4) break;
        }
        return string.Join("\n\n", sections);
    }

    static string Dropped(int distant, OrganizerDrops? organizer) =>
        (distant == 0 ? "" : $"; {distant} moves under distant topics dropped") + organizer;

    // Work cancelled by its caller's deadline has already recorded its outcome; disposal only waits for it.
    public async ValueTask DisposeAsync()
    {
        try { await Drain(); }
        catch (OperationCanceledException) { }
    }
}
