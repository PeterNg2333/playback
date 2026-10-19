using System.Text.RegularExpressions;

namespace Playback.Domain.Notes;

public sealed class NoteBlock
{
    public string Id { get; set; } = "";
    public int Order { get; set; }
    public string Role { get; set; } = "legacy";
    public string Body { get; set; } = "";
    public bool UserEdited { get; set; }
    public string? SupersedesBlockId { get; set; }
    public List<string> CitationIds { get; set; } = [];
    public List<PointAnchor> Anchors { get; set; } = [];
    public List<string> RetainedBlockIds { get; set; } = [];
}
// Legacy coverage identities point into the one canonical body, without copying prose.
public sealed class PointAnchor
{
    public string Id { get; set; } = "";
    public int Offset { get; set; }
    public int Length { get; set; }
    public List<string> SourceIds { get; set; } = [];
}
public sealed class NoteTreeOperation
{
    public string Kind { get; set; } = "";
    public string TopicId { get; set; } = "";
    public string? ParentId { get; set; }
    public string BlockId { get; set; } = "";
    public int Order { get; set; }
    public string Title { get; set; } = "";
    public string Role { get; set; } = "detail";
    public string Body { get; set; } = "";
    public string? SupersedesBlockId { get; set; }
    public List<string> SourceIds { get; set; } = [];
    // replace-text: the exact text in BlockId and what replaces it. delete-block: why the block goes.
    public string Find { get; set; } = "";
    public string Replace { get; set; } = "";
    public string Reason { get; set; } = "";
}
public sealed record RejectedOperation(NoteTreeOperation Operation, string Reason);
public static class NoteTree
{
    public static NoteSection Canonical(NoteSection section, NoteSection? previous = null)
    {
        var result = new NoteSection { Id = section.Id, SchemaVersion = 2, Title = section.Title, ParentId = section.ParentId, Order = section.Order,
            Version = section.Version, UserEdited = section.UserEdited, OrganizedAt = section.OrganizedAt, OrganizedVersion = section.OrganizedVersion };
        if (section.Blocks.Count > 0) result.Blocks = section.Blocks.Select(Clone).ToList();
        else
        {
            if (section.SchemaVersion == 2) return result;
            if (string.IsNullOrWhiteSpace(section.Markdown)) return result;
            var body = Regex.Replace(section.Markdown, @"\A#{1,4}\s+[^\r\n]+\r?\n\s*\n?", "");
            if (string.IsNullOrWhiteSpace(body)) body = section.Markdown;
            var block = new NoteBlock { Id = "block_" + ContentHash.Of(section.Id + ":" + body)[..20], Body = body,
                UserEdited = section.UserEdited, CitationIds = CitationSyntax.Bodies(body).Distinct().ToList() };
            foreach (var point in section.Points)
            {
                var offset = body.IndexOf(point.Text, StringComparison.Ordinal);
                block.Anchors.Add(new() { Id = point.Id, Offset = Math.Max(0, offset), Length = offset < 0 ? body.Length : point.Text.Length, SourceIds = point.SourceIds.ToList() });
            }
            result.Blocks.Add(block);
            if (previous?.Blocks.Count > 0)
            {
                var positions = previous.Blocks.Select(x => (Block: x, Offset: body.IndexOf(x.Body, StringComparison.Ordinal))).OrderBy(x => x.Offset).ToList();
                if (positions.All(x => x.Offset >= 0) && positions.Zip(positions.Skip(1)).All(pair => pair.First.Offset + pair.First.Block.Body.Length <= pair.Second.Offset))
                {
                    result.Blocks.Clear(); var cursor = 0;
                    void Gap(int end)
                    {
                        var text = body[cursor..end].Trim();
                        if (text.Length == 0) return;
                        var addition = new NoteBlock { Id = "block_" + ContentHash.Of(section.Id + ":" + cursor + ":" + text)[..20], Body = text,
                            Order = result.Blocks.Count, UserEdited = section.UserEdited, CitationIds = CitationSyntax.Bodies(text).Distinct().ToList() };
                        foreach (var point in section.Points)
                        {
                            var offset = text.IndexOf(point.Text, StringComparison.Ordinal);
                            if (offset >= 0) addition.Anchors.Add(new() { Id = point.Id, Offset = offset, Length = point.Text.Length, SourceIds = point.SourceIds.ToList() });
                        }
                        result.Blocks.Add(addition);
                    }
                    foreach (var position in positions) { Gap(position.Offset); var retained = Clone(position.Block); retained.Order = result.Blocks.Count; result.Blocks.Add(retained); cursor = position.Offset + retained.Body.Length; }
                    Gap(body.Length);
                }
            }
        }
        return result;
    }
    public static NoteBlock Clone(NoteBlock block) => new() { Id = block.Id, Order = block.Order, Role = block.Role, Body = block.Body,
        UserEdited = block.UserEdited, SupersedesBlockId = block.SupersedesBlockId, CitationIds = block.CitationIds.ToList(), RetainedBlockIds = block.RetainedBlockIds.ToList(),
        Anchors = block.Anchors.Select(x => new PointAnchor { Id = x.Id, Offset = x.Offset, Length = x.Length, SourceIds = x.SourceIds.ToList() }).ToList() };
    public static List<NoteBlock> Compact(string sessionId, IEnumerable<NoteBlock> blocks, IEnumerable<string> allowed, List<NoteCitation> citations) => blocks.Select(original =>
    {
        var block = Clone(original); block.Body = NoteSections.Compact(sessionId, original.Body, allowed, citations);
        for (var i = 0; i < block.Anchors.Count; i++)
        {
            var anchor = original.Anchors[i];
            var text = NoteSections.Compact(sessionId, original.Body.Substring(anchor.Offset, anchor.Length), allowed, citations);
            var offset = block.Body.IndexOf(text, StringComparison.Ordinal);
            if (offset < 0) throw new InvalidOperationException("Compacted block lost a coverage anchor");
            block.Anchors[i].Offset = offset; block.Anchors[i].Length = text.Length;
        }
        block.CitationIds = CitationSyntax.Bodies(block.Body).Distinct().ToList(); return block;
    }).ToList();
    public static List<(NoteSection Topic, string Number, int Depth)> Outline(IEnumerable<NoteSection> topics)
    {
        var list = topics.ToList(); Validate(list);
        var result = new List<(NoteSection, string, int)>();
        void Visit(string? parent, string prefix, int depth)
        {
            var i = 0;
            foreach (var topic in list.Where(x => x.ParentId == parent).OrderBy(x => x.Order).ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                var number = prefix + ++i; result.Add((topic, number, depth)); Visit(topic.Id, number + ".", depth + 1);
            }
        }
        Visit(null, "", 1); return result;
    }
    public static string Render(IEnumerable<NoteSection> topics, bool numbered) => string.Join("\n\n", Outline(topics).Select(row =>
        row.Topic.SchemaVersion < 2 && row.Topic.Blocks.Count == 0 && !string.IsNullOrWhiteSpace(row.Topic.Markdown) ? row.Topic.Markdown.Trim() : new string('#', row.Depth + 1) + " " + (numbered ? row.Number + " " : "") + row.Topic.Title + "\n\n" + string.Join("\n\n", row.Topic.Blocks.OrderBy(x => x.Order).Select(x => x.Body)))).Trim();
    public static void Validate(IReadOnlyList<NoteSection> topics)
    {
        if (topics.Count > 512 || topics.Select(x => x.Id).Distinct().Count() != topics.Count) throw new InvalidOperationException("Duplicate topics or topic budget exceeded");
        var known = topics.ToDictionary(x => x.Id);
        var blocks = new HashSet<string>();
        foreach (var topic in topics)
        {
            if (topic.Order < 0 || topic.Title.Length > 200) throw new InvalidOperationException("Invalid topic order/title");
            var seen = new HashSet<string> { topic.Id }; var parent = topic.ParentId;
            while (parent is not null)
            {
                if (!known.TryGetValue(parent, out var node) || !seen.Add(parent) || seen.Count > 3) throw new InvalidOperationException("Unknown parent, cycle or topic depth exceeds three");
                parent = node.ParentId;
            }
            foreach (var block in topic.Blocks)
            {
                if (!blocks.Add(block.Id) || block.Body.Length is < 1 or > 250000 || block.Order < 0 || block.Anchors.Any(x => x.Offset < 0 || x.Length < 0 || x.Offset + (long)x.Length > block.Body.Length)) throw new InvalidOperationException("Invalid block identity/body/anchor");
            }
        }
    }
    // All or nothing: the first operation the tree refuses rejects the whole reply (organization uses this).
    public static List<NoteSection> Apply(IEnumerable<NoteSection> previous, IReadOnlyList<NoteTreeOperation> operations,
        IEnumerable<string> allowedSources, string inputHash, bool allowProtected = false, bool structuralOnly = false, int maxOperations = MaxOperations)
    {
        if (operations.Count > maxOperations) throw new InvalidOperationException("Tree operation budget exceeded");
        var tree = new Editor(previous, allowedSources, inputHash, allowProtected, structuralOnly);
        foreach (var op in operations) tree.Apply(op);
        Validate(tree.Topics);
        tree.DropEmptyNewTopics();
        return tree.Topics;
    }

    // Like an editor applying a list of edits: each operation applies on its own, and one the tree refuses is
    // reported and skipped while the rest still apply. Operations that depend on a refused one fail in turn.
    public static (List<NoteSection> Topics, List<RejectedOperation> Rejected) ApplyEach(IEnumerable<NoteSection> previous,
        IReadOnlyList<NoteTreeOperation> operations, IEnumerable<string> allowedSources, string inputHash)
    {
        var tree = new Editor(previous, allowedSources, inputHash, allowProtected: false, structuralOnly: false);
        var rejected = operations.Skip(MaxOperations).Select(x => new RejectedOperation(x, "Tree operation budget exceeded")).ToList();
        foreach (var op in operations.Take(MaxOperations))
        {
            var before = tree.Snapshot();
            try { tree.Apply(op); Validate(tree.Topics); }
            catch (InvalidOperationException ex) { tree.Restore(before); rejected.Add(new(op, ex.Message)); }
        }
        rejected.AddRange(tree.DropEmptyNewTopics().Select(x => new RejectedOperation(x, "A new topic without content was not kept")));
        return (tree.Topics, rejected);
    }

    public const int MaxOperations = 24;
    public const int MaxOrganizationOperations = 80;

    // A reply names new topics and blocks with any short alias (the prompt suggests "new:slug"); stored IDs are
    // derived from it, so the alias only has to be unambiguous within the reply and differ from existing IDs.
    static bool IsAlias(string id) => Regex.IsMatch(id, @"^[^\s\[\]]{1,80}$");

    sealed class Editor(IEnumerable<NoteSection> previous, IEnumerable<string> allowedSources, string inputHash, bool allowProtected, bool structuralOnly)
    {
        public List<NoteSection> Topics = previous.Select(x => Canonical(x)).ToList();
        Dictionary<string, string> aliases = [];
        List<(NoteTreeOperation Op, string Id)> created = [];
        readonly HashSet<string> allowed = allowedSources.ToHashSet();

        public (List<NoteSection>, Dictionary<string, string>, List<(NoteTreeOperation, string)>) Snapshot() =>
            (Topics.Select(x => Canonical(x)).ToList(), new(aliases), created.ToList());
        public void Restore((List<NoteSection> Topics, Dictionary<string, string> Aliases, List<(NoteTreeOperation, string)> Created) state) =>
            (Topics, aliases, created) = (state.Topics, state.Aliases, state.Created);

        string Resolve(string id) => aliases.GetValueOrDefault(id, id);
        NoteSection Topic(string id) => Topics.SingleOrDefault(x => x.Id == Resolve(id)) ?? throw new InvalidOperationException("Unknown topic");

        public void Apply(NoteTreeOperation op)
        {
            if (op.Order < 0 || op.Order > 10000) throw new InvalidOperationException("Invalid sibling order");
            if (op.Kind == "create-topic") { CreateTopic(op); return; }
            if (op.Kind is "replace-text" or "delete-block") { EditBlock(op); return; }
            var topic = Topic(op.TopicId);
            if (op.Kind == "rename-topic")
            {
                if (topic.UserEdited && !allowProtected) throw new InvalidOperationException("Protected topic requires an explicit scope action");
                if (op.Title.Trim().Length is < 1 or > 200) throw new InvalidOperationException("Invalid topic title");
                topic.Title = op.Title.Trim(); topic.Version++; return;
            }
            if (op.Kind == "move-topic" || op.Kind == "reorder-topic")
            {
                if (topic.UserEdited && !allowProtected) throw new InvalidOperationException("Protected topic requires an explicit scope action");
                if (op.Kind == "move-topic") topic.ParentId = op.ParentId is null ? null : Topic(op.ParentId).Id;
                topic.Order = op.Order; topic.Version++; return;
            }
            if (op.Kind == "add-block")
            {
                if (structuralOnly || !IsAlias(op.BlockId) || Topics.Any(x => x.Blocks.Any(b => b.Id == op.BlockId)) || op.Body.Trim().Length is < 1 or > 8000 || op.SourceIds.Count == 0 || op.SourceIds.Except(allowed).Any()) throw new InvalidOperationException("Invalid new evidence block");
                if (NoteContent.IsPlaceholder(op.Body)) throw new InvalidOperationException("A placeholder is not note content");
                if (op.Role is not ("definition" or "example" or "derivation" or "correction" or "detail")) throw new InvalidOperationException("Unknown block role");
                if (op.SupersedesBlockId is not null && !Topics.SelectMany(x => x.Blocks).Any(x => x.Id == op.SupersedesBlockId || x.RetainedBlockIds.Contains(op.SupersedesBlockId))) throw new InvalidOperationException("Unknown correction target");
                var cited = NoteSections.Sources(op.Body, allowed);
                if (op.SourceIds.Except(cited).Any()) throw new InvalidOperationException("New block lacks observable citations");
                var id = "point_" + ContentHash.Of(inputHash + op.BlockId)[..20];
                if (Topics.Any(x => x.Blocks.Any(b => b.Id == id))) throw new InvalidOperationException("Duplicate new block alias");
                topic.Blocks.Add(new() { Id = id, Body = op.Body, Order = op.Order, Role = op.Role, SupersedesBlockId = op.SupersedesBlockId,
                    CitationIds = CitationSyntax.Bodies(op.Body).Distinct().ToList(), Anchors = [new() { Id = id, Length = op.Body.Length, SourceIds = op.SourceIds.Distinct().ToList() }] });
                topic.Version++; return;
            }
            var (sourceTopic, block) = ProtectedCheck(op);
            if (op.Kind is "move-block" or "reorder-block")
            {
                sourceTopic.Blocks.Remove(block); topic.Blocks.Add(block); block.Order = op.Order; sourceTopic.Version++; if (sourceTopic != topic) topic.Version++;
            }
            else if (op.Kind == "update-block")
            {
                if (structuralOnly || op.Body != block.Body) throw new InvalidOperationException("Body rewriting requires a retained-block preview");
            }
            else throw new InvalidOperationException("Unknown tree operation");
        }

        // A topic whose title already exists under the same parent receives the content instead of a duplicate.
        void CreateTopic(NoteTreeOperation op)
        {
            if (!IsAlias(op.TopicId) || aliases.ContainsKey(op.TopicId) || Topics.Any(x => x.Id == op.TopicId) || op.Title.Trim().Length is < 1 or > 200) throw new InvalidOperationException("Invalid new topic");
            var parent = op.ParentId is null ? null : Topic(op.ParentId).Id;
            var title = op.Title.Trim();
            var existing = Topics.FirstOrDefault(x => x.ParentId == parent && string.Equals(x.Title.Trim(), title, StringComparison.OrdinalIgnoreCase));
            if (existing is not null) { aliases.Add(op.TopicId, existing.Id); return; }
            var id = "section_" + ContentHash.Of(inputHash + op.TopicId)[..20];
            aliases.Add(op.TopicId, id);
            Topics.Add(new() { Id = id, Title = title, ParentId = parent, Order = op.Order });
            created.Add((op, id));
        }

        // Text edits find their block by ID alone; a topic ID in the reply is not needed.
        void EditBlock(NoteTreeOperation op)
        {
            if (structuralOnly) throw new InvalidOperationException("Structural organization cannot edit text");
            var (topic, block) = ProtectedCheck(op);
            if (op.Kind == "replace-text") NoteContent.ReplaceOnce(block, op.Find, op.Replace);
            else topic.Blocks.Remove(block);
            topic.Version++;
        }

        (NoteSection Topic, NoteBlock Block) ProtectedCheck(NoteTreeOperation op)
        {
            var topic = Topics.SingleOrDefault(x => x.Blocks.Any(b => b.Id == op.BlockId)) ?? throw new InvalidOperationException("Unknown block");
            var block = topic.Blocks.Single(x => x.Id == op.BlockId);
            if ((topic.UserEdited || block.UserEdited) && !allowProtected) throw new InvalidOperationException("Protected block requires an explicit scope action");
            return (topic, block);
        }

        // Created topics that ended with neither blocks nor subtopics are removed, innermost first.
        public List<NoteTreeOperation> DropEmptyNewTopics()
        {
            var dropped = new List<NoteTreeOperation>();
            bool removed;
            do
            {
                removed = false;
                foreach (var (op, id) in created.ToList())
                {
                    var topic = Topics.SingleOrDefault(x => x.Id == id);
                    if (topic is null || topic.Blocks.Count > 0 || Topics.Any(x => x.ParentId == id)) continue;
                    Topics.Remove(topic); created.Remove((op, id)); dropped.Add(op); removed = true;
                }
            } while (removed);
            return dropped;
        }
    }
}
