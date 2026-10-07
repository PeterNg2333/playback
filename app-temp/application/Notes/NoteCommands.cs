using Playback.Domain;
using Playback.Domain.Notes;

namespace Playback.Application.Notes;

// User edits, historical reads, explicit restore and selected recovery. History is append-only.
public sealed class NoteCommands(INoteRepository repository, INoteEvidence evidence)
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
        var sections = NoteSections.UserEdit(session.CurrentNote, id, historical.Markdown, session.AllowedSources, citations, deleted, suppressed);
        foreach (var section in sections) section.UserEdited = true;
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
}
