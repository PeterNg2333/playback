using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Playback.Domain;
using Playback.Domain.Notes;

namespace Playback.Application.Notes;

// Writes a session's notes at three speeds (NotePrompts): while recording, a live jot for each finished minute and a segment
// for each finished five minutes; after Stop, an outline and a polish per top-level topic. Segments are the record the AI
// keeps; topics are laid out from them (NoteLayout). A segment whose transcript changed (a correction or a session rerun) is
// written again, and only the finished topics polished from it are polished again: lineage decides what an update touches.
// Every change is saved as a new note version through INoteRepository.
public sealed class NoteWorkflow : IAsyncDisposable
{
    const int MaxConcurrentCalls = 4;
    const int MaxInstructionLength = 2_000;
    const int MaxMaterialChars = 12_000;
    const int MaxRelatedBlocks = 8;
    static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15)];
    static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(1);
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    readonly ConcurrentDictionary<string, SemaphoreSlim> sessionLocks = new();
    readonly ConcurrentDictionary<string, (int Attempts, DateTime RetryAt)> failures = new();
    readonly SemaphoreSlim callSlots = new(MaxConcurrentCalls, MaxConcurrentCalls);
    readonly INoteRepository repository;
    readonly INoteEvidence evidence;
    readonly INoteWriter writer;
    readonly INoteActivity activity;
    readonly INoteScript? script;
    readonly TimeProvider clock;
    readonly NoteScheduler scheduler;

    public NoteWorkflow(INoteRepository repository, INoteEvidence evidence, INoteWriter writer, INoteActivity activity,
        INoteScript? script = null, TimeProvider? clock = null)
    {
        this.repository = repository;
        this.evidence = evidence;
        this.writer = writer;
        this.activity = activity;
        this.script = script;
        this.clock = clock ?? TimeProvider.System;
        scheduler = new NoteScheduler(this.clock, Automatic, interval: TimeSpan.FromSeconds(2));
    }

    public sealed record InstructionResult(object Note, int Applied, List<string> NotApplied);

    public bool Due => scheduler.Due;
    public void Tick(IEnumerable<string> ids, CancellationToken ct) => scheduler.Tick(ids, ct);
    public Task Drain() => Task.WhenAll(scheduler.Active);

    // One step per tick: the oldest due segment, else the last minute's jot, else the finish. Every window counts as closed
    // after Stop, or once no new speech has been confirmed for a minute (so corrections after the lecture are written too).
    // Only Stop outlines the whole lecture; when idle, finished topics are polished again if a segment they came from changed.
    // A failed step waits 10 s, 1 min, 5 min, then 15 min before it is tried again, so a failing provider is not paid every tick.
    async Task Automatic(string id, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (!writer.IsConfigured || failures.TryGetValue(id, out var failed) && failed.RetryAt > now) return;
        var sessionLock = SessionLock(id);
        if (!await sessionLock.WaitAsync(0, ct)) return;
        try
        {
            var gate = await repository.Gate(id);
            var stopped = gate?.FlushRequested == true;
            var session = await evidence.Session(id);
            if (session is null) return;
            var lastSpeech = session.Transcripts.Max(x => x.ConfirmedAt);
            var idle = lastSpeech is null || now - lastSpeech > IdleAfter;
            var closeAll = (stopped || idle) && !session.AwaitingTranscripts;
            var plan = NoteTimeline.Plan(session, closeAll);
            if (plan.Segments.Count > 0) await WriteSegment(session, plan.Segments[0], ct);
            else if (plan.JotMinute is { } minute) await WriteJot(session, minute, ct);
            else if (closeAll && stopped)
            {
                await Finish(session, ct, outline: true);
                await repository.AcknowledgeFlush(id, gate!.FlushVersion);
            }
            else if (closeAll) await Finish(session, ct, outline: false);
            failures.TryRemove(id, out _);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            var attempts = failures.TryGetValue(id, out var previous) ? previous.Attempts + 1 : 1;
            failures[id] = (attempts, clock.GetUtcNow().UtcDateTime + Backoff[Math.Min(attempts, Backoff.Length) - 1]);
            activity.Warning("Note job failed", id, ex);
        }
        finally { sessionLock.Release(); }
    }

    // Revise with AI: write every window that is due as if the lecture had stopped, then finish.
    public Task<object> Generate(string id, CancellationToken ct) => Locked(id, ct, async () =>
    {
        await WriteDue(id, ct);
        return await Finish(await Session(id), ct);
    });

    // After a correction or a session rerun: rewrite the windows whose transcript changed and polish what cited them.
    public Task<int> WritePending(string id, CancellationToken ct) => Locked(id, ct, async () =>
    {
        var written = await WriteDue(id, ct);
        if (written > 0) await Finish(await Session(id), ct);
        return written;
    });

    // Writes the notes again from the whole transcript. Edited and locked blocks stay (in the topic they were in); history
    // keeps every earlier version.
    public Task<NoteRebuildResult> Rebuild(string id, CancellationToken ct) => Locked(id, ct, async () =>
    {
        var session = await Session(id);
        if (session.Transcripts.All(x => string.IsNullOrWhiteSpace(x.SourceText)))
            throw new InvalidOperationException("No transcript to write notes from");
        var start = session.NoteVersion;
        var kept = session.Sections.Select(NoteTree.Clone).Select(topic =>
        {
            topic.Blocks = topic.Blocks.Where(b => b.UserEdited).ToList();
            topic.SegmentBlockIds = [];
            return topic;
        }).Where(x => x.Blocks.Count > 0 || x.UserEdited).ToList();
        await repository.Save(id, new SectionUpdate(kept, NoteSections.CloneCitations(session.CurrentNote?.Citations ?? []),
                Segments: [], Summary: [], ReviewQuestions: []),
            ContentHash.Of("rebuild:" + start + ":" + clock.GetUtcNow().Ticks), start, session.NoteLanguage, "user rebuild");
        await evidence.MarkNotes(session.Transcripts.Select(x => x.Id), "pending");
        var written = await WriteDue(id, ct);
        await Finish(await Session(id), ct);
        var final = await Session(id);
        return new NoteRebuildResult(start, final.NoteVersion, written, NoteTimeline.Plan(final, closeAll: true).Segments.Count);
    });

    // The owner's instruction for one block, one topic, or every top-level topic. It is applied directly because a person
    // asked for it; history keeps the previous version, and locked blocks stay as they are.
    public Task<InstructionResult> Instruct(string id, string instruction, string? topicId, string? blockId, int baseVersion, CancellationToken ct)
    {
        instruction = instruction.Trim();
        if (instruction.Length is < 1 or > MaxInstructionLength) throw new InvalidOperationException("Write an instruction of up to 2,000 characters");
        if (!writer.IsConfigured) throw new InvalidOperationException("The note model is not configured; no instruction was sent");
        return Locked(id, ct, async () =>
        {
            var session = await Session(id);
            if (session.NoteVersion != baseVersion) throw new InvalidOperationException("Notes changed; reload and give the instruction again");
            var note = session.CurrentNote ?? throw new InvalidOperationException("There are no notes to revise yet");
            var topics = note.Sections.Select(NoteTree.Clone).ToList();
            List<(NoteSection Topic, NoteBlock? Block)> targets = blockId is not null
                ? topics.Where(t => t.Blocks.Any(b => b.Id == blockId)).Select(t => (t, (NoteBlock?)t.Blocks.First(b => b.Id == blockId))).ToList()
                : topics.Where(t => topicId is not null ? t.Id == topicId : t.ParentId is null && t.Id != NoteLayout.LiveTopicId)
                    .Take(12).Select(t => (t, (NoteBlock?)null)).ToList();
            if (targets.Count == 0) throw new InvalidOperationException(blockId is not null ? "Block not found" : "Topic not found");
            var notApplied = new List<string>();
            var applied = 0;
            foreach (var (topic, block) in targets)
            {
                var editable = block is null ? topic.Blocks.Where(b => !b.UserEdited).ToList() : block.UserEdited ? [] : [block];
                if (editable.Count == 0) { notApplied.Add($"{topic.Title}: locked"); continue; }
                var markdown = await RewriteText(session, note, topic, editable, instruction, ct);
                if (markdown is null) { notApplied.Add($"{topic.Title}: the model returned no text"); continue; }
                var (body, sources) = markdown.Value;
                if (block is not null) Replace(topic, block.Id, NoteLayout.Block(block.Id, block.Role, body, sources, block.Order));
                else
                {
                    var rewritten = NoteLayout.Block("r-" + ContentHash.Of(topic.Id + body)[..12], "detail", body, sources);
                    topic.Blocks = topic.Blocks.Where(b => b.UserEdited).Prepend(rewritten).Select((b, i) => { b.Order = i; return b; }).ToList();
                }
                applied++;
            }
            if (applied == 0) return new InstructionResult(Current(session), 0, notApplied);
            var saved = await repository.Save(id, new SectionUpdate(topics, NoteSections.CloneCitations(note.Citations)),
                ContentHash.Of("instruction:" + session.NoteVersion + instruction + topicId + blockId), session.NoteVersion, session.NoteLanguage, "user instruction");
            return new InstructionResult(saved, applied, notApplied);
        });
    }

    // A transcript correction cannot race a note call that is still writing from the old wording.
    public Task<bool> UpdateEvidence(string id, Func<Task<bool>> save, CancellationToken ct) => Locked(id, ct, save);

    async Task<int> WriteDue(string id, CancellationToken ct)
    {
        var written = 0;
        // Each window is written once per pass; a window that fails to change is not retried here.
        var tried = new HashSet<int>();
        while (NoteTimeline.Plan(await Session(id), closeAll: true).Segments.FirstOrDefault(x => !tried.Contains(x.Index)) is { } window)
        {
            ct.ThrowIfCancellationRequested();
            tried.Add(window.Index);
            await WriteSegment(await Session(id), window, ct);
            written++;
        }
        return written;
    }

    // ── Live jot ──

    sealed record JotReply(List<JotPoint> Points);
    sealed record JotPoint(string Text, List<string> Sources);

    async Task WriteJot(NoteSession session, int minute, CancellationToken ct)
    {
        var lines = NoteTimeline.Lines(session);
        var current = lines.Where(x => x.At / NoteTimeline.MinuteMs == minute).ToList();
        var recent = lines.Where(x => x.At >= minute * NoteTimeline.MinuteMs - 3 * NoteTimeline.MinuteMs && x.At < minute * NoteTimeline.MinuteMs).ToList();
        var aliases = new NoteAliases(recent.Concat(current).Select(x => x.Id));
        var input = Prefix(session) + "\n" + JsonSerializer.Serialize(new { recent = aliases.Rows(recent), @new = aliases.Rows(current) }, Json);
        var (reply, call) = await Call<JotReply>(session, NotePrompts.Jot, "Live jot", NotePrompts.JotInstructions(session.NoteLanguage), input,
            current.Select(x => x.Id), ct);
        try
        {
            var points = reply.Points.Select(p => (Text: InScript(aliases.Decode(p.Text), session), Sources: aliases.Sources(p.Sources).ToList()))
                .Where(p => !NoteContent.IsPlaceholder(p.Text)).ToList();
            if (points.Count == 0) { await activity.End(call, "completed", "Nothing to jot for this minute"); return; }
            var body = string.Join("\n", points.Select(p => "- " + p.Text.Trim()));
            var live = NoteLayout.Live(session.CurrentNote).Append(NoteLayout.Block(NoteLayout.LiveBlockId(minute), "live", body,
                points.SelectMany(p => p.Sources))).ToList();
            await Save(session, NoteLayout.Arrange(session.CurrentNote, session.CurrentNote?.Segments ?? [], live, session.NoteLanguage),
                null, "notes live", "jot:" + minute + ":" + body);
            await activity.End(call, "completed", $"{points.Count} live points for {NoteTimeline.Clock(minute * NoteTimeline.MinuteMs)}");
        }
        catch (Exception ex) { await activity.Fail(call, ex); throw; }
    }

    // ── Segment ──

    sealed record SegmentReply(string Title, List<SegmentBlock> Blocks, List<SegmentConcept> Concepts, List<SegmentCorrection> Corrections);
    sealed record SegmentBlock(string Kind, string Markdown, List<string> Sources);
    sealed record SegmentConcept(string Name, string Definition, List<string> Sources);
    sealed record SegmentCorrection(string Heard, string Written, List<string> Sources);

    async Task WriteSegment(NoteSession session, NoteWindow window, CancellationToken ct)
    {
        var note = session.CurrentNote;
        var before = (note?.Segments ?? []).Where(x => x.Index < window.Index).OrderBy(x => x.Index).ToList();
        var aliases = new NoteAliases(window.Lines.Select(x => x.Id));
        var input = Prefix(session) + "\n" + JsonSerializer.Serialize(new
        {
            outline = before.Select(s => NoteTimeline.Clock(s.StartMs) + " " + s.Title),
            previous = before.LastOrDefault() is { } last ? new { last.Title, notes = string.Join("\n\n", last.Blocks.Select(b => Uncited(b.Body))) } : null,
            window = aliases.Rows(window.Lines)
        }, Json);
        var (reply, call) = await Call<SegmentReply>(session, NotePrompts.Segment, "Note segment", NotePrompts.SegmentInstructions(session.NoteLanguage),
            input, window.Lines.Select(x => x.Id), ct);
        try
        {
            var blocks = reply.Blocks.Select(b => (b.Kind, Body: InScript(aliases.Decode(b.Markdown), session), Sources: aliases.Sources(b.Sources).ToList()))
                .Where(b => !NoteContent.IsPlaceholder(b.Body))
                .Select((b, i) => NoteLayout.Block(NoteLayout.SegmentBlockId(window.Index, ContentHash.Of(window.Hash + "\n" + i + "\n" + b.Body)), b.Kind, b.Body, b.Sources, i))
                .ToList();
            var segment = new NoteSegment
            {
                Index = window.Index, StartMs = window.StartMs, EndMs = window.EndMs, InputHash = window.Hash,
                Title = InScript(reply.Title.Trim(), session), SourceIds = window.Lines.Select(x => x.Id).ToList(), Blocks = blocks,
                Concepts = reply.Concepts.Select(c => new NoteConcept { Name = c.Name.Trim(), Definition = InScript(c.Definition, session),
                    SourceIds = aliases.Sources(c.Sources).ToList() }).Where(c => c.Name.Length > 0).ToList(),
                Corrections = reply.Corrections.Select(c => new NoteCorrection { Heard = c.Heard, Written = c.Written,
                    SourceIds = aliases.Sources(c.Sources).ToList() }).ToList()
            };
            var segments = (note?.Segments ?? []).Where(x => x.Index != window.Index).Append(segment).OrderBy(x => x.Index).ToList();
            // The window's jots are now covered by its segment.
            var live = NoteLayout.Live(note).Where(x => NoteLayout.MinuteOf(x.Id) is not { } m || m * NoteTimeline.MinuteMs / NoteTimeline.WindowMs != window.Index).ToList();
            await Save(session, NoteLayout.Arrange(note, segments, live, session.NoteLanguage), segments, "notes segment", "segment:" + window.Hash + ":" + JsonSerializer.Serialize(reply, Json));
            await evidence.MarkNotes(window.Lines.Select(x => x.Id), "completed");
            await activity.End(call, "completed", $"{blocks.Count} blocks for {NoteTimeline.Clock(window.StartMs)} \"{segment.Title}\"");
        }
        catch (Exception ex) { await activity.Fail(call, ex); throw; }
    }

    // ── Finish: outline and polish ──

    sealed record OutlineReply(List<OutlineEntry> Topics, List<string> Summary, List<string> ReviewQuestions);
    sealed record OutlineEntry(string Key, string Title, string Parent, List<string> Blocks);
    sealed record PolishReply(List<PolishedTopic> Topics);
    sealed record PolishedTopic(string Key, string Markdown);

    // With outline, and no finished topics yet or segments no finished topic covers: outline and polish everything. Otherwise
    // only the finished topics whose segment blocks were rewritten are polished again. Returns the saved (or current) note.
    async Task<object> Finish(NoteSession session, CancellationToken ct, bool outline = true)
    {
        var note = session.CurrentNote;
        var segments = note?.Segments ?? [];
        if (note is null || segments.Count == 0) return Current(session);
        var topics = note.Sections.Select(NoteTree.Clone).ToList();
        var finished = topics.Where(NoteLayout.IsFinished).ToList();
        var placed = finished.SelectMany(x => x.SegmentBlockIds).Select(NoteLayout.SegmentOf).OfType<int>().ToHashSet();
        // A segment without blocks (only greetings or filler) has nothing to place.
        var written = segments.Where(x => x.Blocks.Count > 0).ToList();
        if (written.Count == 0) return Current(session);
        if (outline && (finished.Count == 0 || written.Any(x => !placed.Contains(x.Index)))) return await Outline(session, note, ct);
        var current = segments.SelectMany(x => x.Blocks).Select(x => x.Id).ToHashSet();
        var stale = finished.Where(t => t.SegmentBlockIds.Any(id => !current.Contains(id))).ToList();
        if (stale.Count == 0) return Current(session);
        // A rewritten segment's new blocks go to the topic that held most of its old blocks; other topics just lose the old IDs.
        foreach (var index in stale.SelectMany(t => t.SegmentBlockIds).Where(id => !current.Contains(id)).Select(NoteLayout.SegmentOf).OfType<int>().Distinct().ToList())
        {
            var owner = stale.MaxBy(t => t.SegmentBlockIds.Count(id => NoteLayout.SegmentOf(id) == index))!;
            foreach (var topic in stale) topic.SegmentBlockIds.RemoveAll(id => NoteLayout.SegmentOf(id) == index);
            owner.SegmentBlockIds.AddRange(segments.First(x => x.Index == index).Blocks.Select(x => x.Id));
        }
        var blocks = segments.SelectMany(x => x.Blocks).ToDictionary(x => x.Id);
        var polished = await Polish(session, stale.Select(t => (t, 1, t.SegmentBlockIds.Select(id => blocks[id]).ToList())).ToList(), ct);
        foreach (var topic in stale) topic.Blocks = PolishedBlocks(topic, polished.GetValueOrDefault(topic.Id), topic.SegmentBlockIds.Select(id => blocks[id]));
        return await Save(session, topics, null, "notes final", "repolish:" + string.Join("|", stale.Select(t => t.Id + string.Join(",", t.SegmentBlockIds))));
    }

    async Task<object> Outline(NoteSession session, Note note, CancellationToken ct)
    {
        var segments = note.Segments.Where(x => x.Blocks.Count > 0).OrderBy(x => x.Index).ToList();
        var blocks = segments.SelectMany(s => s.Blocks.Select(b => (Segment: s, Block: b))).ToList();
        var keys = blocks.Select((x, i) => (Key: "B" + (i + 1), x.Block.Id)).ToDictionary(x => x.Key, x => x.Id);
        var input = Prefix(session) + "\n" + JsonSerializer.Serialize(new
        {
            blocks = blocks.Select((x, i) => new object[] { "B" + (i + 1), NoteTimeline.Clock(x.Segment.StartMs), x.Segment.Title, x.Block.Role,
                Regex.Replace(Uncited(x.Block.Body), @"\s+", " ").Trim() is var text && text.Length > 160 ? text[..160] : text })
        }, Json);
        var (reply, call) = await Call<OutlineReply>(session, NotePrompts.Outline, "Note outline", NotePrompts.OutlineInstructions(session.NoteLanguage),
            input, segments.SelectMany(s => s.SourceIds), ct);
        try
        {
            var outline = NoteLayout.RepairOutline(reply.Topics.Select(t => new NoteLayout.OutlineTopic(t.Key, InScript(t.Title, session), t.Parent,
                t.Blocks.Where(keys.ContainsKey).Select(k => keys[k]).ToList())), blocks.Select(x => x.Block.Id).ToList());
            var ids = outline.ToDictionary(t => t.Key, t => NoteLayout.TopicId(session.Id, [t.Key, t.Title, .. t.Blocks]));
            var byId = blocks.ToDictionary(x => x.Block.Id, x => x.Block);
            var topics = outline.Select(t => new NoteSection { Id = ids[t.Key], Title = t.Title,
                ParentId = t.Parent.Length > 0 ? ids[t.Parent] : null, SegmentBlockIds = t.Blocks.ToList() }).ToList();
            foreach (var group in topics.GroupBy(x => x.ParentId)) { var order = 0; foreach (var topic in group) topic.Order = order++; }
            int Depth(NoteSection t) => t.ParentId is null ? 1 : 1 + Depth(topics.First(x => x.Id == t.ParentId));
            var polished = await Polish(session, topics.Select(t => (t, Depth(t), t.SegmentBlockIds.Select(id => byId[id]).ToList())).ToList(), ct);
            foreach (var topic in topics) topic.Blocks = PolishedBlocks(topic, polished.GetValueOrDefault(topic.Id), topic.SegmentBlockIds.Select(id => byId[id]));
            // Edited and locked blocks keep their text: one the finish wrote again (same ID) is replaced by the edited version,
            // any other joins the topic that cites most of the same sources.
            foreach (var edited in NoteLayout.Edited(note.Sections.Select(NoteTree.Clone)))
            {
                if (topics.FirstOrDefault(t => t.Blocks.Any(b => b.Id == edited.Id)) is { } rewritten)
                {
                    Replace(rewritten, edited.Id, edited);
                    continue;
                }
                var sources = NoteLayout.Sources(edited, note.Citations);
                var home = topics.MaxBy(t => t.SegmentBlockIds.Select(id => byId[id]).SelectMany(b => b.SourceIds).Intersect(sources).Count())!;
                edited.Order = home.Blocks.Count;
                home.Blocks.Add(edited);
            }
            var saved = await Save(session, topics, null, "notes final", "outline:" + JsonSerializer.Serialize(reply, Json),
                reply.Summary.Select(x => InScript(aliasesFree(x), session)).ToList(), reply.ReviewQuestions.Select(x => InScript(x, session)).ToList());
            await activity.End(call, "completed", $"{topics.Count} topics ({topics.Count(x => x.ParentId is null)} top level) from {blocks.Count} blocks");
            return saved;
        }
        catch (Exception ex) { await activity.Fail(call, ex); throw; }
        static string aliasesFree(string text) => Regex.Replace(text, @"\s*\[(?:T|B)\d+(?:\s*,\s*(?:T|B)\d+)*\]", "");
    }

    // One polish call per top-level topic (with its subtopics), at most four at a time. A reply that keeps under 80% of a
    // topic's citations or half its length is not used: that topic keeps its segment blocks.
    async Task<Dictionary<string, string>> Polish(NoteSession session, List<(NoteSection Topic, int Depth, List<NoteBlock> Blocks)> topics, CancellationToken ct)
    {
        var result = new ConcurrentDictionary<string, string>();
        var roots = topics.Where(x => x.Topic.ParentId is null || topics.All(p => p.Topic.Id != x.Topic.ParentId)).ToList();
        List<(NoteSection Topic, int Depth, List<NoteBlock> Blocks)> Subtree(NoteSection root) =>
            topics.Where(x => x.Topic == root).Concat(topics.Where(x => x.Topic.ParentId == root.Id).SelectMany(x => Subtree(x.Topic))).ToList();
        await Task.WhenAll(roots.Select(async root =>
        {
            var scope = Subtree(root.Topic);
            if (scope.All(x => x.Blocks.Count == 0)) return;
            var aliases = new NoteAliases(scope.SelectMany(x => x.Blocks).SelectMany(b => b.SourceIds));
            var original = scope.ToDictionary(x => x.Topic.Id, x => aliases.Encode(string.Join("\n\n", x.Blocks.Select(b => b.Body))));
            var keys = scope.Select((x, i) => (Key: "k" + (i + 1), x.Topic.Id)).ToDictionary(x => x.Key, x => x.Id);
            var input = Prefix(session) + "\n" + JsonSerializer.Serialize(new
            {
                topics = scope.Select((x, i) => new { key = "k" + (i + 1), title = x.Topic.Title, depth = x.Depth, notes = original[x.Topic.Id] })
            }, Json);
            PolishReply reply;
            NoteActivity call;
            // A failed polish (already recorded in activity) leaves these topics with their segment blocks.
            try
            {
                (reply, call) = await Call<PolishReply>(session, NotePrompts.Polish, "Note polish", NotePrompts.PolishInstructions(session.NoteLanguage),
                    input, scope.SelectMany(x => x.Blocks).SelectMany(b => b.SourceIds), ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested) { return; }
            var kept = 0;
            foreach (var item in reply.Topics.Where(x => keys.ContainsKey(x.Key)))
            {
                var id = keys[item.Key];
                var before = Regex.Matches(original[id], @"\bT\d+\b").Select(m => m.Value).ToHashSet();
                var after = Regex.Matches(item.Markdown, @"\bT\d+\b").Select(m => m.Value).ToHashSet();
                var cited = before.Count == 0 ? 1 : before.Count(after.Contains) / (double)before.Count;
                if (cited < 0.8 || item.Markdown.Length < original[id].Length * 0.5) continue;
                result[id] = InScript(aliases.Decode(Regex.Replace(item.Markdown, @"^#{1,6}\s+(.+?)\s*$", "**$1**", RegexOptions.Multiline)), session);
                kept++;
            }
            await activity.End(call, "completed", $"{kept} of {scope.Count} topics polished; the rest keep their segment blocks");
        }));
        return new(result);
    }

    static List<NoteBlock> PolishedBlocks(NoteSection topic, string? polished, IEnumerable<NoteBlock> segmentBlocks)
    {
        var blocks = polished is null
            ? segmentBlocks.Select(b => NoteLayout.Block(b.Id, b.Role, b.Body, b.SourceIds)).ToList()
            : [NoteLayout.Block("p-" + ContentHash.Of(topic.Id + "\n" + polished)[..12], "detail", polished,
                segmentBlocks.SelectMany(b => b.SourceIds))];
        var edited = topic.Blocks.Where(b => b.UserEdited).ToList();
        return blocks.Where(b => edited.All(e => e.Id != b.Id)).Concat(edited).Select((b, i) => { b.Order = i; return b; }).ToList();
    }

    // ── Rewrite on request ──

    sealed record RewriteReply(string Markdown);

    // Related notes come from the concept graph: segment concepts whose sources the target cites name the concepts, and other
    // segment blocks that mention them are given as context, with their transcript lines, so the rewrite can cite them.
    async Task<(string Body, List<string> Sources)?> RewriteText(NoteSession session, Note note, NoteSection topic, List<NoteBlock> target,
        string instruction, CancellationToken ct)
    {
        var cited = target.SelectMany(b => NoteLayout.Sources(b, note.Citations)).ToHashSet(StringComparer.Ordinal);
        var names = note.Segments.SelectMany(s => s.Concepts).Where(c => c.SourceIds.Any(cited.Contains)).Select(c => c.Name).Distinct().ToList();
        var related = note.Segments.SelectMany(s => s.Blocks)
            .Where(b => !b.SourceIds.Any(cited.Contains))
            .Select(b => (Block: b, Hits: names.Count(n => b.Body.Contains(n, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.Hits > 0).OrderByDescending(x => x.Hits).Take(MaxRelatedBlocks).Select(x => x.Block).ToList();
        var lines = NoteTimeline.Lines(session);
        var sources = cited.Concat(related.SelectMany(b => b.SourceIds)).ToHashSet(StringComparer.Ordinal);
        var used = lines.Where(x => sources.Contains(x.Id)).ToList();
        var aliases = new NoteAliases(used.Select(x => x.Id));
        var text = string.Join("\n\n", target.Select(b => aliases.Encode(NoteSections.Expand(b.Body, note.Citations))));
        var input = Prefix(session) + "\n" + JsonSerializer.Serialize(new
        {
            instruction,
            target = new { kind = target.Count == 1 && topic.Blocks.Count > 1 ? "block" : "topic", topic.Title, markdown = text },
            transcript = aliases.Rows(used),
            related = related.Select(b => aliases.Encode(b.Body))
        }, Json);
        var (reply, call) = await Call<RewriteReply>(session, NotePrompts.Rewrite, "Note instruction", NotePrompts.RewriteInstructions(session.NoteLanguage),
            input, cited, ct);
        var body = InScript(aliases.Decode(reply.Markdown), session).Trim();
        if (NoteContent.IsPlaceholder(body)) { await activity.End(call, "completed", "No text returned; nothing changed"); return null; }
        await activity.End(call, "completed", $"Rewrote {topic.Title} with {related.Count} related notes as context");
        return (body, NoteSections.Sources(body, used.Select(x => x.Id)));
    }

    static void Replace(NoteSection topic, string blockId, NoteBlock block) =>
        topic.Blocks = topic.Blocks.Select(b => b.Id == blockId ? block : b).ToList();

    // ── Shared ──

    // One model call, recorded in AI activity. The caller ends the activity once the result is saved.
    async Task<(T Reply, NoteActivity Call)> Call<T>(NoteSession session, NoteTask task, string label, string instructions, string input,
        IEnumerable<string> sources, CancellationToken ct)
    {
        var call = await activity.Begin(session.Id, label, writer.Provider, writer.Model, sources, session.NoteVersion);
        activity.Context(call, NotePrompts.Version, instructions, input);
        await callSlots.WaitAsync(ct);
        try
        {
            await activity.Start(call);
            var watch = Stopwatch.StartNew();
            string reply;
            try { reply = await writer.Complete(task, instructions, input, ct, text => activity.Draft(call, ReadableDraft(text)), usage => call.UsageJson = usage); }
            finally { call.ProviderLatencyMs = watch.ElapsedMilliseconds; }
            return (JsonSerializer.Deserialize<T>(reply, Json) ?? throw new InvalidOperationException("The note model returned an empty reply"), call);
        }
        catch (Exception ex) { await activity.Fail(call, ex); throw; }
        finally { callSlots.Release(); }
    }

    Task<object> Save(NoteSession session, List<NoteSection> topics, List<NoteSegment>? segments, string author, string identity,
        List<string>? summary = null, List<string>? reviewQuestions = null) =>
        repository.Save(session.Id, new SectionUpdate(topics, NoteSections.CloneCitations(session.CurrentNote?.Citations ?? []),
                Segments: segments, Summary: summary, ReviewQuestions: reviewQuestions),
            ContentHash.Of(author + "\n" + session.NoteVersion + "\n" + identity), session.NoteVersion, session.NoteLanguage, author);

    // The fixed start of every input, so providers can cache it: the session's materials (course context).
    static string Prefix(NoteSession session)
    {
        var budget = MaxMaterialChars;
        var materials = new List<object>();
        foreach (var material in session.Materials)
        {
            if (budget <= 0) break;
            var text = material.Text.Length > budget ? material.Text[..budget] : material.Text;
            budget -= text.Length;
            materials.Add(new { material.Name, text });
        }
        return JsonSerializer.Serialize(new { course = new { materials } }, Json);
    }

    static string Uncited(string text) => Regex.Replace(CitationSyntax.Rewrite(text, _ => ""), @"[ \t]*\[\]", "");
    string InScript(string text, NoteSession session) => script?.Apply(text, session.NoteLanguage) ?? text;
    static object Current(NoteSession session) => new { Version = session.NoteVersion, Markdown = session.NoteMarkdown, Author = session.CurrentNote?.Author };
    async Task<NoteSession> Session(string id) => await evidence.Session(id) ?? throw new InvalidOperationException("Session not found");
    SemaphoreSlim SessionLock(string id) => sessionLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));

    async Task<T> Locked<T>(string id, CancellationToken ct, Func<Task<T>> work)
    {
        var sessionLock = SessionLock(id);
        await sessionLock.WaitAsync(ct);
        try { return await work(); }
        finally { sessionLock.Release(); }
    }

    // The streamed reply is JSON; the activity popover shows the Markdown inside it as it arrives.
    public static string ReadableDraft(string json)
    {
        var parts = new List<string>();
        foreach (Match match in Regex.Matches(json, "\"(?:markdown|text|title)\"\\s*:\\s*\"(?<body>(?:\\\\.|[^\"\\\\])*)", RegexOptions.IgnoreCase))
        {
            try { parts.Add(JsonSerializer.Deserialize<string>("\"" + match.Groups["body"].Value + "\"") ?? ""); }
            catch (JsonException) { /* an incomplete escape becomes readable on the next streaming update */ }
        }
        return string.Join("\n\n", parts);
    }

    // Work cancelled by its caller's deadline has already recorded its outcome; disposal only waits for it.
    public async ValueTask DisposeAsync()
    {
        try { await Drain(); }
        catch (OperationCanceledException) { }
    }
}

public sealed record NoteRebuildResult(int StartVersion, int Version, int Rounds, int RemainingSources);
