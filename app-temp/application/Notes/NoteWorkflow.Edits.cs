using Playback.Domain;
using Playback.Domain.Notes;

namespace Playback.Application.Notes;

// How one note reply becomes changes without an all-or-nothing gate, the way an editor applies a list of edits:
// added content applies at once, a small fact-keeping text edit applies by NoteEditPolicy, and a riskier edit
// waits as a suggestion while the rest of the reply still applies. Earlier text is never replaced otherwise.
public sealed partial class NoteWorkflow
{
    // ClosedKept: blocks aimed at a closed topic that stayed there because the reply declared no subtopic for them.
    sealed record EditOutcome(int Appended, int AutoEdited, int Suggested, int RepeatDeferrals, int Redirected, int ClosedKept, List<RejectedOperation> Dropped);

    async Task<EditOutcome> TriageEdits(NoteSession session, NotePatch patch, List<string> allowed, List<NoteTranscript> pending)
    {
        // A source may wait for its continuation once; a second deferral is ignored and the source stays pending.
        var repeated = patch.Deferred.Where(x => session.Transcripts.Any(t => t.Id == x.SourceId && t.NoteStatus == "deferred")).ToList();
        foreach (var item in repeated) patch.Deferred.Remove(item);
        var dropped = new List<RejectedOperation>();
        var appended = AppendToExistingTopics(session, patch, dropped);
        var (redirected, closedKept) = RedirectFromClosedTopics(session, patch, pending);
        foreach (var legacy in patch.Operations.Where(x => x.Kind == "update-block").ToList())
        {
            patch.Operations.Remove(legacy);
            dropped.Add(new(legacy, "update-block is retired; use replace-text"));
        }
        RejectUnknownReferences(session, patch, allowed, dropped);
        var edits = patch.Operations.Where(x => x.Kind is "replace-text" or "delete-block").ToList();
        var deletes = edits.Count(x => x.Kind == "delete-block");
        var autoEdited = 0;
        var suggested = 0;
        foreach (var edit in edits)
        {
            var topic = session.Sections.FirstOrDefault(s => NoteTree.Canonical(s).Blocks.Any(b => b.Id == edit.BlockId));
            if (topic is null || session.CurrentNote is null) continue; // the tree reports the unknown block
            var block = NoteTree.Canonical(topic).Blocks.Single(b => b.Id == edit.BlockId);
            if (topic.UserEdited || block.UserEdited) continue; // the tree refuses protected text
            var after = "";
            if (edit.Kind == "replace-text")
            {
                var copy = NoteTree.Clone(block);
                try { NoteContent.ReplaceOnce(copy, edit.Find, edit.Replace); }
                catch (InvalidOperationException ex) { patch.Operations.Remove(edit); dropped.Add(new(edit, ex.Message)); continue; }
                after = copy.Body;
            }
            var reason = edit.Kind != "delete-block" ? NoteEditPolicy.TextEditReview(edit.Find, edit.Replace)
                : deletes > NoteEditPolicy.MaxAutomaticDeletes ? $"{deletes} deletions in one reply"
                : LosesLastCitation(session, patch, block) ? "the deletion would remove a source's last citation" : null;
            if (reason is null) { autoEdited++; continue; }
            patch.Operations.Remove(edit);
            if (rewrites is null) { dropped.Add(new(edit, reason + "; suggestions are unavailable")); continue; }
            var change = new RewriteBlockChange { TargetBlockId = block.Id, RetainedBlockIds = [block.Id], Role = block.Role,
                Kind = edit.Kind == "delete-block" ? "delete" : "replace", Body = after, Reason = $"{edit.Reason} ({reason})".Trim() };
            await rewrites.SaveRewrite(NoteRewrite.Preview(session.CurrentNote, topic.Id, [change], allowed, clock.GetUtcNow().UtcDateTime));
            suggested++;
        }
        return new(appended, autoEdited, suggested, repeated.Count, redirected, closedKept, dropped);
    }

    const int MaxRegroupGapMinutes = 5;

    // The organizer groups by subject, but only with nearby lecture: moving a topic under an existing topic that
    // was spoken more than five minutes away is dropped (it put a system-call topic under a study-method one).
    // Grouping under a parent created in the same reply is unaffected. Returns how many moves were dropped.
    static int DropDistantMoves(NotePatch patch, NoteSession session)
    {
        var spoken = session.Transcripts.ToDictionary(x => x.Id, x => x.StartMs);
        (long From, long To)? Range(string id)
        {
            var times = session.Sections.Where(x => x.Id == id || NoteStructure.IsWithin(x, id, session.Sections))
                .SelectMany(x => x.Blocks).SelectMany(x => x.Anchors).SelectMany(x => x.SourceIds).Where(spoken.ContainsKey).Select(x => spoken[x]).ToList();
            return times.Count == 0 ? null : (times.Min(), times.Max());
        }
        var created = patch.Operations.Where(x => x.Kind == "create-topic").Select(x => x.TopicId).ToHashSet();
        var distant = patch.Operations.Where(x => x.Kind == "move-topic" && x.ParentId is { } parent && !created.Contains(parent) &&
            Range(x.TopicId) is { } moved && Range(parent) is { } target &&
            Math.Max(target.From - moved.To, moved.From - target.To) > MaxRegroupGapMinutes * 60_000L).ToList();
        foreach (var move in distant) patch.Operations.Remove(move);
        return distant.Count;
    }

    // What an organizer reply named that cannot apply, by cause, so a run shows whether the model reached outside its
    // scope or only left out where a block goes. In the saved Qwen runs every dropped move named an in-scope block:
    // the destination was missing, "" or the block's own topic, never another topic.
    sealed record OrganizerDrops(int OutOfScope, int WithoutDestination, int InPlace)
    {
        public override string ToString() => (OutOfScope == 0 ? "" : $"; {OutOfScope} operations outside the selected scope dropped") +
            (WithoutDestination == 0 ? "" : $"; {WithoutDestination} moves without a destination dropped") +
            (InPlace == 0 ? "" : $"; {InPlace} moves into the topic the block is already in ignored");
    }

    // Organization works on the selected topic and its subtopics. An operation reaching outside them is dropped
    // rather than failing the whole regrouping, together with the moves into a new topic that was dropped. A move
    // with no destination, or into the topic its block is already in, changes nothing and is dropped as well.
    static OrganizerDrops DropOutOfScope(NotePatch patch, List<NoteSection> context)
    {
        var scope = context.Select(x => x.Id).ToHashSet();
        var home = context.SelectMany(topic => topic.Blocks.Select(block => (Block: block.Id, Topic: topic.Id))).DistinctBy(x => x.Block).ToDictionary(x => x.Block, x => x.Topic);
        var created = new HashSet<string>();
        int outOfScope = 0, withoutDestination = 0, inPlace = 0;
        foreach (var operation in patch.Operations.ToList())
        {
            bool Known(string? id) => id is null || scope.Contains(id) || created.Contains(id);
            if (operation.Kind == "move-block" && string.IsNullOrWhiteSpace(operation.TopicId)) { patch.Operations.Remove(operation); withoutDestination++; continue; }
            if (operation.Kind == "move-block" && home.GetValueOrDefault(operation.BlockId) == operation.TopicId) { patch.Operations.Remove(operation); inPlace++; continue; }
            var inside = operation.Kind == "create-topic" ? Known(operation.ParentId)
                : Known(operation.TopicId) && Known(operation.ParentId) && (operation.BlockId.Length == 0 || operation.Kind == "add-block" || home.ContainsKey(operation.BlockId));
            if (!inside) { patch.Operations.Remove(operation); outOfScope++; }
            else if (operation.Kind == "create-topic") created.Add(operation.TopicId);
        }
        return new(outOfScope, withoutDestination, inPlace);
    }

    public static readonly string[] NewTopicKeys = ["new1", "new2", "new3", "new4", "new5", "new6", "new7", "new8"];

    // The placement plan becomes tree operations: each declared topic a create-topic (parents first, so a new
    // subtopic can sit under a new topic), each block an add-block in the topic it names, and each of the
    // organizer's moves a move-block into the topic it names. Runs before decoding.
    static void ExpandPlacements(NotePatch patch)
    {
        string Alias(string key) => "new:" + key;
        bool IsNew(string key) => NewTopicKeys.Contains(key);
        var declared = patch.Outline.Where(x => IsNew(x.Key)).GroupBy(x => x.Key).Select(x => x.First()).ToList();
        var ordered = new List<OutlinePlacement>();
        void Visit(OutlinePlacement item, int depth)
        {
            if (ordered.Contains(item) || depth > NoteStructure.MaxDepth) return;
            if (declared.FirstOrDefault(x => x.Key == item.Parent && x != item) is { } parent) Visit(parent, depth + 1);
            ordered.Add(item);
        }
        foreach (var item in declared) Visit(item, 0);
        var operations = ordered.Select(item => new NoteTreeOperation { Kind = "create-topic", TopicId = Alias(item.Key), Title = item.Title,
            ParentId = item.Parent.Length == 0 ? null : IsNew(item.Parent) ? Alias(item.Parent) : item.Parent }).ToList();
        operations.AddRange(patch.Blocks.Select((block, i) => new NoteTreeOperation { Kind = "add-block", BlockId = "new:block" + (i + 1),
            TopicId = IsNew(block.Topic) ? Alias(block.Topic) : block.Topic, Role = block.Role, Body = block.Body, SourceIds = block.SourceIds.ToList(), Order = i }));
        operations.AddRange(patch.Moves.Select(move => new NoteTreeOperation { Kind = "move-block", BlockId = move.Block, TopicId = IsNew(move.To) ? Alias(move.To) : move.To }));
        // A rename without a title changes nothing; models send one for a topic their outline just named.
        patch.Operations.RemoveAll(x => x.Kind == "rename-topic" && string.IsNullOrWhiteSpace(x.Title));
        // A move, rename or reorder may name a new key as its target; a move-topic to parent "" goes to the top
        // level, as "" means in outline.
        foreach (var operation in patch.Operations)
        {
            if (IsNew(operation.TopicId)) operation.TopicId = Alias(operation.TopicId);
            if (operation.ParentId is { } parent && IsNew(parent)) operation.ParentId = Alias(parent);
            if (operation.Kind == "move-topic" && operation.ParentId?.Length == 0) operation.ParentId = null;
        }
        patch.Operations.InsertRange(0, operations);
        patch.Outline.Clear(); patch.Blocks.Clear(); patch.Moves.Clear();
    }

    // New text from the model (bodies, titles, replacements) in the note's script. find stays as written: it has to
    // match the existing block exactly.
    void InScript(NotePatch patch, string language)
    {
        if (script is null) return;
        foreach (var operation in patch.Operations)
        {
            operation.Body = script.Apply(operation.Body, language);
            operation.Title = script.Apply(operation.Title, language);
            operation.Replace = script.Apply(operation.Replace, language);
        }
    }

    // A create-topic that carries its own body and sources is a new topic plus its first block.
    // Runs before decoding, so the new block's aliases and citations are decoded like any other block.
    static void SplitTopicContent(NotePatch patch)
    {
        foreach (var topic in patch.Operations.Where(x => x.Kind == "create-topic" && !string.IsNullOrWhiteSpace(x.Body)).ToList())
        {
            var block = new NoteTreeOperation { Kind = "add-block", TopicId = topic.TopicId, BlockId = topic.TopicId + ":body", Role = "detail",
                Body = topic.Body, SourceIds = topic.SourceIds.ToList() };
            patch.Operations.Insert(patch.Operations.IndexOf(topic) + 1, block);
            topic.Body = ""; topic.SourceIds = [];
        }
    }

    // A reply that sends an existing topic with only new points means "add these", not "replace the topic":
    // the earlier blocks stay as they are, and each new point that brings new evidence is appended as a block.
    int AppendToExistingTopics(NoteSession session, NotePatch patch, List<RejectedOperation> dropped)
    {
        var appended = 0;
        var citations = NoteSections.CloneCitations(session.CurrentNote?.Citations ?? []);
        string Normalize(string body) => NoteSections.Compact(session.Id, NoteSections.Expand(body, citations), session.AllowedSources, citations).Replace("\r\n", "\n");
        foreach (var section in patch.Sections.Where(x => x.Id != "new").ToList())
        {
            var topic = session.Sections.SingleOrDefault(x => x.Id == section.Id);
            if (topic is null) continue; // NoteSections reports the unknown identity
            var blocks = NoteTree.Canonical(topic).Blocks;
            var existing = string.Join("\n\n", blocks.Select(x => Normalize(x.Body)));
            var reply = Normalize(string.Join("\n\n", section.Points.Select(x => x.Text)));
            if (blocks.All(block => reply.Contains(Normalize(block.Body), StringComparison.Ordinal))) continue; // a full copy updates as before
            patch.Sections.Remove(section);
            var cited = blocks.SelectMany(x => x.Anchors).SelectMany(x => x.SourceIds).ToHashSet();
            var order = blocks.Count == 0 ? 0 : blocks.Max(x => x.Order) + 1;
            foreach (var (point, i) in section.Points.Select((point, i) => (point, i)))
            {
                if (existing.Contains(Normalize(point.Text), StringComparison.Ordinal)) continue; // unchanged earlier text
                var op = new NoteTreeOperation { Kind = "add-block", TopicId = topic.Id, Role = "detail", Order = order + i, Body = point.Text,
                    SourceIds = point.SourceIds.ToList(), BlockId = "new:append-" + ContentHash.Of(section.Id + ":" + i + ":" + point.Text)[..16].ToLowerInvariant() };
                if (point.Retains.Count > 0) dropped.Add(new(op, "A changed copy of an existing point was not applied; the earlier text stays"));
                else if (point.SourceIds.All(cited.Contains)) dropped.Add(new(op, "Restates content the topic already cites"));
                else { patch.Operations.Add(op); appended++; }
            }
        }
        return appended;
    }

    // One unknown citation or term reference drops only the operation that contains it.
    static void RejectUnknownReferences(NoteSession session, NotePatch patch, List<string> allowed, List<RejectedOperation> dropped)
    {
        var known = allowed.Concat(session.CurrentNote?.Citations.Select(x => x.Id) ?? []).ToList();
        foreach (var op in patch.Operations.Where(x => x.Kind is "add-block" or "replace-text").ToList())
        {
            try
            {
                var text = op.Kind == "add-block" ? op.Body : op.Replace;
                NoteReferences.ValidateReferences(text, session.TermReferenceIds);
                NoteReferences.ValidateSourceReferences(text, known);
            }
            catch (InvalidOperationException ex) { patch.Operations.Remove(op); dropped.Add(new(op, ex.Message)); }
        }
    }

    // A closed topic (full or long-running) takes no new blocks, and the schema does not offer it, so the writer
    // continues its subject in a subtopic it declares with a heading title. A block a reply still aims at a closed
    // topic goes into the subtopic the same reply declared for its continuation: under the closed topic, or beside
    // it when that is already three levels deep. Without one the block stays where the reply put it; the topic is
    // then over six blocks and the organizer splits it with titles of its own. The application never makes up a
    // title (it used to take the block's first sentence), and nothing is refused, so no call is lost.
    static (int Redirected, int Kept) RedirectFromClosedTopics(NoteSession session, NotePatch patch, List<NoteTranscript> pending)
    {
        var closed = NoteInput.ClosedTopics(session, pending);
        int redirected = 0, kept = 0;
        foreach (var op in patch.Operations.Where(x => x.Kind == "add-block" && closed.Contains(x.TopicId)).ToList())
        {
            var topic = session.Sections.Single(x => x.Id == op.TopicId);
            var parent = NoteStructure.Depth(topic, session.Sections) < NoteStructure.MaxDepth ? topic.Id : topic.ParentId;
            var declared = patch.Operations.FirstOrDefault(x => x.Kind == "create-topic" && x.ParentId == parent);
            if (declared is null) { kept++; continue; }
            op.TopicId = declared.TopicId;
            redirected++;
        }
        return (redirected, kept);
    }

    // A deletion may run on its own only when every source it cites stays cited somewhere else.
    static bool LosesLastCitation(NoteSession session, NotePatch patch, NoteBlock block)
    {
        var deleted = patch.Operations.Where(x => x.Kind == "delete-block").Select(x => x.BlockId).ToHashSet();
        var elsewhere = session.Sections.SelectMany(x => NoteTree.Canonical(x).Blocks).Where(x => !deleted.Contains(x.Id))
            .SelectMany(x => x.Anchors).SelectMany(x => x.SourceIds)
            .Concat(patch.Operations.Where(x => x.Kind == "add-block").SelectMany(x => x.SourceIds)).ToHashSet();
        return block.Anchors.SelectMany(x => x.SourceIds).Any(x => !elsewhere.Contains(x));
    }

    // Notes follow the lecture: after an AI change, sibling topics are ordered by when their first source was
    // spoken (topics without lecture sources keep their place after them). Order numbers in a reply, or moves
    // by the organizer, are only the model's guess. A group containing a protected topic is not reshuffled;
    // only topics new in this change are slotted into it.
    static void KeepLectureOrder(List<NoteSection> topics, HashSet<string> existing, NoteSession session)
    {
        // Topics an AI change emptied (the organizer moved their blocks away) go, unless protected or holding subtopics.
        while (topics.FirstOrDefault(x => x.Blocks.Count == 0 && !x.UserEdited && topics.All(child => child.ParentId != x.Id)) is { } empty)
            topics.Remove(empty);
        var spoken = session.Transcripts.ToDictionary(x => x.Id, x => (x.RecordedAt ?? DateTime.MinValue, x.StartMs));
        (DateTime, long)? FirstSpoken(NoteSection topic)
        {
            var times = topic.Blocks.SelectMany(x => x.Anchors).SelectMany(x => x.SourceIds).Where(spoken.ContainsKey).Select(x => spoken[x])
                .Concat(topics.Where(x => x.ParentId == topic.Id).Select(FirstSpoken).OfType<(DateTime, long)>()).ToList();
            return times.Count == 0 ? null : times.Min();
        }
        foreach (var group in topics.GroupBy(x => x.ParentId).Where(x => x.All(topic => !topic.UserEdited)))
        {
            var ordered = group.OrderBy(x => FirstSpoken(x) is null).ThenBy(FirstSpoken).ThenBy(x => x.Order).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
            for (var i = 0; i < ordered.Count; i++) ordered[i].Order = i;
        }
        foreach (var topic in topics.Where(x => !existing.Contains(x.Id) && topics.Any(s => s.ParentId == x.ParentId && s.UserEdited)).OrderBy(FirstSpoken))
        {
            if (FirstSpoken(topic) is not { } time) continue;
            var siblings = topics.Where(x => x.ParentId == topic.ParentId && x.Id != topic.Id)
                .OrderBy(x => x.Order).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
            var later = siblings.FirstOrDefault(x => FirstSpoken(x) is { } other && other.CompareTo(time) > 0);
            if (later is null) { topic.Order = siblings.Count == 0 ? 0 : siblings.Max(x => x.Order) + 1; continue; }
            var slot = later.Order;
            foreach (var sibling in siblings.Where(x => x.Order >= slot)) sibling.Order++;
            topic.Order = slot;
        }
    }
}
