using Playback.Domain;
using Playback.Domain.Evidence;
using Playback.Domain.Notes;

namespace Playback.Application.Notes;

// What the owner does to their notes: read history, edit the whole draft or one block, lock a block, restore a version.
// Every change is a new version; history is append-only.
public sealed class NoteCommands(INoteRepository repository, INoteEvidence evidence)
{
    const int MaxNoteLength = 250_000;
    const int MaxBlockLength = 32_000;

    public Task<List<Note>> History(string id) => repository.History(id);
    public Task<Note?> Version(string id, int version) => repository.Version(id, version);
    public Task<object> Page(string id, int? before, int limit = 20) => repository.Page(id, before, limit);

    public async Task<object> SaveNote(string id, string markdown, string author, int? basedOnVersion = null)
    {
        if (markdown.Length > MaxNoteLength) throw new InvalidOperationException("Note is too large");
        var session = await Session(id);
        if (basedOnVersion.HasValue && basedOnVersion != session.NoteVersion)
            throw new InvalidOperationException("Notes changed; keep your editor text and compare with the latest version");
        var citations = NoteSections.CloneCitations(session.CurrentNote?.Citations ?? []);
        var sections = NoteSections.UserEdit(session.CurrentNote, id, markdown, session.AllowedSources, citations);
        return await repository.Save(id, new SectionUpdate(sections, citations),
            ContentHash.Of($"user:{session.NoteVersion}:{markdown}"), session.NoteVersion, session.NoteLanguage, author);
    }

    // The old version's topics come back as the owner's own (locked) text; the segments the AI keeps are not changed.
    public async Task<object> RestoreNote(string id, int version, int? basedOnVersion)
    {
        var session = await Session(id);
        if (basedOnVersion is not null && session.NoteVersion != basedOnVersion) throw new InvalidOperationException("Notes changed; reload before restoring");
        var historical = Lineage.Apply(await repository.Version(id, version) ?? throw new InvalidOperationException("Historical note not found"), session.Supersessions);
        if (historical.Sections.All(x => x.Blocks.Count == 0))
            throw new InvalidOperationException("This version was saved before topic notes and cannot be restored; rebuild the notes instead");
        var citations = NoteSections.CloneCitations(session.CurrentNote?.Citations ?? []);
        foreach (var citation in historical.Citations.Where(x => citations.All(c => c.Id != x.Id))) citations.Add(citation);
        var sections = historical.Sections.Select(NoteTree.Clone).ToList();
        foreach (var section in sections) { section.UserEdited = true; foreach (var block in section.Blocks) block.UserEdited = true; }
        return await repository.Save(id, new SectionUpdate(sections, citations), ContentHash.Of($"restore:{version}:{session.NoteVersion}"),
            session.NoteVersion, session.NoteLanguage, "user restore");
    }

    public async Task<object> EditBlock(string id, int basedOnVersion, string blockId, string body)
    {
        var session = await Session(id);
        if (session.NoteVersion != basedOnVersion) throw new InvalidOperationException("Notes changed; keep your block draft and compare with the latest version");
        if (body.Trim().Length is < 1 or > MaxBlockLength) throw new InvalidOperationException("Invalid block body size");
        var (sections, topic, block) = Find(session, blockId);
        var citations = NoteSections.CloneCitations(session.CurrentNote?.Citations ?? []);
        var allowed = session.AllowedSources.Concat(citations.SelectMany(x => x.SourceIds)).Distinct().ToList();
        NoteReferences.ValidateReferences(body, session.TermReferenceIds);
        NoteReferences.ValidateSourceReferences(body, allowed.Concat(citations.Select(x => x.Id)));
        block.Body = NoteSections.Compact(id, body, allowed, citations);
        block.UserEdited = true;
        block.SourceIds = NoteSections.Sources(NoteSections.Expand(body, citations), allowed);
        block.CitationIds = CitationSyntax.Bodies(block.Body).Distinct().ToList();
        topic.Version++;
        return await repository.Save(id, new(sections, citations), ContentHash.Of("block-edit:" + basedOnVersion + blockId + body), basedOnVersion, session.NoteLanguage, "user block edit");
    }

    // A locked block is never changed or removed by automatic writing or instructions (the same protection a user edit
    // gives); unlocking lets AI change it again. Each toggle is a saved version.
    public async Task<object> LockBlock(string id, int basedOnVersion, string blockId, bool locked)
    {
        var session = await Session(id);
        if (session.NoteVersion != basedOnVersion) throw new InvalidOperationException("Notes changed; reload before locking");
        var (sections, topic, block) = Find(session, blockId);
        if (block.UserEdited == locked) return new { Version = session.NoteVersion, Markdown = session.NoteMarkdown };
        block.UserEdited = locked;
        topic.Version++;
        return await repository.Save(id, new(sections, NoteSections.CloneCitations(session.CurrentNote?.Citations ?? [])),
            ContentHash.Of("block-lock:" + basedOnVersion + blockId + locked), basedOnVersion, session.NoteLanguage, locked ? "user lock" : "user unlock");
    }

    // A copy of the current topics, and the topic and block to change in it.
    static (List<NoteSection> Sections, NoteSection Topic, NoteBlock Block) Find(NoteSession session, string blockId)
    {
        var sections = session.Sections.Select(NoteTree.Clone).ToList();
        var topic = sections.SingleOrDefault(x => x.Blocks.Any(b => b.Id == blockId)) ?? throw new InvalidOperationException("Block not found");
        return (sections, topic, topic.Blocks.Single(x => x.Id == blockId));
    }

    async Task<NoteSession> Session(string id) => await evidence.Session(id) ?? throw new InvalidOperationException("Session not found");
}
