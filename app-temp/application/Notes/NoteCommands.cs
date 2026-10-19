using Playback.Domain;
using Playback.Domain.Notes;

namespace Playback.Application.Notes;

// User edits, historical reads, explicit restore and selected recovery. History is append-only.
public sealed class NoteCommands(INoteRepository repository, INoteEvidence evidence, INoteRewriteRepository? rewrites = null)
{
    public Task<List<Note>> History(string id) => repository.History(id);
    public Task<Note?> Version(string id, int version) => repository.Version(id, version);
    public Task<object> Page(string id, int? before, int limit = 20) => repository.Page(id, before, limit);
    public async Task<NoteCoverageReport?> Coverage(string id) => await evidence.Session(id) is { } session ? NoteCoverage.Audit(session) : null;
    public async Task<object> SaveNote(string id, string markdown, string author, int? basedOnVersion = null)
    {
        if (markdown.Length > 250_000) throw new InvalidOperationException("Note is too large");
        var session = await evidence.Session(id) ?? throw new InvalidOperationException("Session not found");
        if (basedOnVersion.HasValue && basedOnVersion != session.NoteVersion)
            throw new InvalidOperationException("Notes changed; keep your editor text and compare with the latest version");
        var previous = session.CurrentNote;
        var citations = NoteSections.CloneCitations(previous?.Citations ?? []);
        var deleted = previous?.DeletedSectionIds.ToList() ?? [];
        var suppressed = previous?.SuppressedSourceIds.ToList() ?? [];
        var allowed = session.AllowedSources;
        var sections = NoteSections.UserEdit(previous, id, markdown, allowed, citations, deleted, suppressed);
        return await repository.Save(id, new SectionUpdate(sections, citations, []),
            ContentHash.Of($"user:{session.NoteVersion}:{markdown}"), session.NoteVersion, session.NoteLanguage,
            author, deleted, suppressed);
    }
    public async Task<RecoveryPreview> Recovery(string id, int version) {
        var session = await evidence.Session(id) ?? throw new InvalidOperationException("Session not found");
        var historical = await repository.Version(id, version) ?? throw new InvalidOperationException("Historical note not found");
        return NoteSections.Recover(historical, session.CurrentNote, session.AllowedSources);
    }
    public async Task<object> RestoreNote(string id, int version, int? basedOnVersion) {
        var session = await evidence.Session(id) ?? throw new InvalidOperationException("Session not found");
        if (basedOnVersion is not null && session.NoteVersion != basedOnVersion) throw new InvalidOperationException("Notes changed; reload before restoring");
        var historical = await repository.Version(id, version) ?? throw new InvalidOperationException("Historical note not found");
        var citations = NoteSections.CloneCitations(session.CurrentNote?.Citations ?? []);
        foreach (var citation in historical.Citations.Where(x => citations.All(c => c.Id != x.Id))) citations.Add(citation);
        var deleted = session.CurrentNote?.DeletedSectionIds.ToList() ?? [];
        var suppressed = session.CurrentNote?.SuppressedSourceIds.ToList() ?? [];
        var sections = NoteSections.Read(historical, session.AllowedSources).Select(x => NoteTree.Canonical(x)).ToList();
        foreach (var section in sections) { section.UserEdited = true; foreach (var block in section.Blocks) block.UserEdited = true; }
        deleted = deleted.Except(sections.Select(x => x.Id)).ToList();
        suppressed = suppressed.Except(sections.SelectMany(s => s.Points).SelectMany(p => p.SourceIds)).ToList();
        return await repository.Save(id, new SectionUpdate(sections, citations, []), ContentHash.Of($"restore:{version}:{session.NoteVersion}"),
            session.NoteVersion, session.NoteLanguage, "user restore", deleted, suppressed);
    }
    public async Task<object> ApplyRecovery(string id, int version, int basedOnVersion, List<string> selected) {
        var session = await evidence.Session(id) ?? throw new InvalidOperationException("Session not found");
        if (session.NoteVersion != basedOnVersion) throw new InvalidOperationException("Notes changed; reload the recovery preview");
        var preview = await Recovery(id, version);
        var additions = preview.Candidates.Where(x => selected.Contains(x.Id)).ToList();
        if (selected.Count is < 1 or > 20 || additions.Count != selected.Distinct().Count() || additions.Any(x => x.Blocked))
            throw new InvalidOperationException("Invalid or intentionally deleted recovery selection");
        var sections = preview.CurrentSections.Concat(additions.Select(x => new NoteSection { Id = x.Id, Title = x.Title,
            Markdown = x.Markdown, Points = x.Points, UserEdited = true })).ToList();
        return await repository.Save(id, new SectionUpdate(sections, preview.Citations, []),
            ContentHash.Of($"recover:{version}:{basedOnVersion}:" + string.Join(',', selected.Order())), basedOnVersion,
            session.NoteLanguage, "user recovery");
    }
    public async Task<object> ApplyStructure(string id, int basedOnVersion, List<NoteTreeOperation> operations)
    {
        var session = await evidence.Session(id) ?? throw new InvalidOperationException("Session not found");
        if (session.NoteVersion != basedOnVersion) throw new InvalidOperationException("Notes changed; retain your proposed outline and reload");
        if (operations.Count == 0) throw new InvalidOperationException("Select an outline change");
        var hash = ContentHash.Of("structure:" + basedOnVersion + System.Text.Json.JsonSerializer.Serialize(operations));
        var sections = NoteTree.Apply(session.Sections, operations, session.AllowedSources, hash, allowProtected: true, structuralOnly: true);
        var update = new SectionUpdate(sections, NoteSections.CloneCitations(session.CurrentNote?.Citations ?? []), []);
        NoteSections.RequireRetention(session.CurrentNote, update, session.AllowedSources, allowProtectedReformat: true);
        return await repository.Save(id, update, hash, basedOnVersion, session.NoteLanguage, "user structure");
    }
    public async Task<object> EditBlock(string id, int basedOnVersion, string blockId, string body)
    {
        var session = await evidence.Session(id) ?? throw new InvalidOperationException("Session not found");
        if (session.NoteVersion != basedOnVersion) throw new InvalidOperationException("Notes changed; keep your block draft and compare with the latest version");
        if (body.Trim().Length is < 1 or > 32000) throw new InvalidOperationException("Invalid block body size");
        var sections = session.Sections.Select(x => NoteTree.Canonical(x)).ToList();
        var topic = sections.SingleOrDefault(x => x.Blocks.Any(b => b.Id == blockId)) ?? throw new InvalidOperationException("Block not found");
        var block = topic.Blocks.Single(x => x.Id == blockId);
        var citations = NoteSections.CloneCitations(session.CurrentNote?.Citations ?? []);
        var allowed = session.AllowedSources.Concat(citations.SelectMany(x => x.SourceIds)).Distinct().ToList();
        NoteReferences.ValidateReferences(body, session.TermReferenceIds);
        NoteReferences.ValidateSourceReferences(body, allowed.Concat(citations.Select(x => x.Id)));
        var sources = NoteSections.Sources(NoteSections.Expand(body, citations), allowed);
        block.Body = NoteSections.Compact(id, body, allowed, citations); block.UserEdited = true;
        block.Anchors = [new() { Id = block.Id, Length = block.Body.Length, SourceIds = sources }];
        block.CitationIds = CitationSyntax.Bodies(block.Body).Distinct().ToList(); topic.Version++;
        return await repository.Save(id, new(sections, citations, []), ContentHash.Of("block-edit:" + basedOnVersion + blockId + body), basedOnVersion, session.NoteLanguage, "user block edit");
    }
    // A locked block is never edited, moved or deleted by automatic writing or instructions (the same protection
    // a user edit gives); unlocking lets AI change it again. Each toggle is a saved version.
    public async Task<object> LockBlock(string id, int basedOnVersion, string blockId, bool locked)
    {
        var session = await evidence.Session(id) ?? throw new InvalidOperationException("Session not found");
        if (session.NoteVersion != basedOnVersion) throw new InvalidOperationException("Notes changed; reload before locking");
        var sections = session.Sections.Select(x => NoteTree.Canonical(x)).ToList();
        var topic = sections.SingleOrDefault(x => x.Blocks.Any(b => b.Id == blockId)) ?? throw new InvalidOperationException("Block not found");
        var block = topic.Blocks.Single(x => x.Id == blockId);
        if (block.UserEdited == locked) return new { Version = session.NoteVersion, Markdown = session.NoteMarkdown };
        block.UserEdited = locked; topic.Version++;
        return await repository.Save(id, new(sections, NoteSections.CloneCitations(session.CurrentNote?.Citations ?? []), []),
            ContentHash.Of("block-lock:" + basedOnVersion + blockId + locked), basedOnVersion, session.NoteLanguage, locked ? "user lock" : "user unlock");
    }

    public async Task<NoteRewriteProposal> DismissRewrite(string id, string proposalId)
    {
        if (rewrites is null) throw new InvalidOperationException("Suggestions are unavailable");
        var proposal = await rewrites.Rewrite(id, proposalId) ?? throw new InvalidOperationException("Suggestion not found");
        if (proposal.Status == "applied") throw new InvalidOperationException("This suggestion was already applied");
        proposal.Status = "dismissed";
        await rewrites.SaveRewrite(proposal);
        return proposal;
    }

    // A suggestion whose block changed since it was made is listed as "stale" (not saved), matching what Accept refuses.
    public async Task<List<NoteRewriteProposal>> Rewrites(string id)
    {
        if (rewrites is null) return [];
        var proposals = await rewrites.Rewrites(id);
        if ((await evidence.Session(id))?.CurrentNote is { } note)
            foreach (var proposal in proposals.Where(x => x.Status is "review-required" or "blocked-retention" && NoteRewrite.IsStale(note, x)))
                proposal.Status = "stale";
        return proposals;
    }
    public async Task<object> AcceptRewrite(string id, string proposalId, bool semanticAccepted)
    {
        if (rewrites is null) throw new InvalidOperationException("Rewrite previews are unavailable");
        var session = await evidence.Session(id) ?? throw new InvalidOperationException("Session not found");
        var proposal = await rewrites.Rewrite(id, proposalId) ?? throw new InvalidOperationException("Rewrite proposal not found");
        var hash = ContentHash.Of("accepted-rewrite:" + proposal.Id);
        if (session.CurrentNote?.InputHash == hash)
        {
            proposal.Status = "applied"; await rewrites.SaveRewrite(proposal);
            return new {Version = session.NoteVersion, Markdown = session.NoteMarkdown};
        }
        if (session.CurrentNote is null) throw new InvalidOperationException("Notes no longer exist");
        foreach (var change in proposal.Changes) NoteReferences.ValidateReferences(change.Body, session.TermReferenceIds);
        var sections = NoteRewrite.Accept(session.CurrentNote, proposal, session.AllowedSources, semanticAccepted);
        var sources = sections.SelectMany(x => x.Points).SelectMany(x => x.SourceIds).Distinct().ToList();
        var passages = MaterialSources.Passages(session.Materials).ToList();
        var coverage = sources.Select(source => new SourceDisposition {SourceId = source, Status = "covered", Reason = "Explicitly reviewed rewrite",
            ContentHash = ContentHash.Of(session.Transcripts.FirstOrDefault(x => x.Id == source)?.SourceText ?? passages.FirstOrDefault(x => x.Id == source)?.Text ?? ""),
            PointIds = sections.SelectMany(x => x.Points).Where(x => x.SourceIds.Contains(source)).Select(x => x.Id).ToList()}).ToList();
        var result = await repository.Save(id, new(sections, session.CurrentNote.Citations, coverage), hash, session.NoteVersion, session.NoteLanguage, "user accepted rewrite",
            suppressed: session.CurrentNote.SuppressedSourceIds.Except(sources).ToList());
        await evidence.MarkNotes(sources.Intersect(session.Transcripts.Select(x => x.Id)), "completed");
        proposal.Status = "applied"; await rewrites.SaveRewrite(proposal); return result;
    }
}
