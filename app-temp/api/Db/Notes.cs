using Playback.Api.Sources;
using Playback.Api.Notes;
using MongoDB.Driver;
using MongoDB.Bson.Serialization.Attributes;

namespace Playback.Api.Db;

// The note document: every saved version, its sections and citations, and the note gate's decisions.
public sealed class Note
{
    [BsonId] public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public int Version { get; set; }
    public int? BasedOnVersion { get; set; }
    public string Markdown { get; set; } = "";
    public string Author { get; set; } = "";
    public string? OutputLanguage { get; set; }
    public long ProcessedThroughMs { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<string> TranscriptIds { get; set; } = [];
    public List<string> MaterialIds { get; set; } = [];
    public List<string> InputTranscriptIds { get; set; } = [];
    public List<string> InputMaterialIds { get; set; } = [];
    public List<NoteEdit> Edits { get; set; } = [];
    public string? InputHash { get; set; }
    public DateTime? SourceFrom { get; set; }
    public DateTime? SourceThrough { get; set; }
    public List<NoteSection> Sections { get; set; } = [];
    public List<NoteCitation> Citations { get; set; } = [];
    public List<string> DeletedSectionIds { get; set; } = [];
    public List<string> SuppressedSourceIds { get; set; } = [];
    public List<SourceDisposition> Coverage { get; set; } = [];
}
public sealed class NoteSection
{
    public string Id { get; set; } = "";
    public int Version { get; set; } = 1;
    public string Title { get; set; } = "";
    public string Markdown { get; set; } = "";
    public bool UserEdited { get; set; }
    public List<NotePoint> Points { get; set; } = [];
    public DateTime? OrganizedAt { get; set; }
    public int? OrganizedVersion { get; set; }
}
public sealed class NotePoint
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public List<string> SourceIds { get; set; } = [];
}
public sealed class NoteCitation
{
    public string Id { get; set; } = "";
    public List<string> SourceIds { get; set; } = [];
}
public sealed class SourceDisposition
{
    public string SourceId { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string Reason { get; set; } = "";
    public List<string> PointIds { get; set; } = [];
    public string? ContentHash { get; set; }
}
public sealed class NoteEdit
{
    public string Kind { get; set; } = "";
    public int Line { get; set; }
    public string Text { get; set; } = "";
    public List<string> TranscriptIds { get; set; } = [];
    public List<string> MaterialIds { get; set; } = [];
}
public sealed class NoteGateRecord
{
    [BsonId] public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string InputHash { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string Decision { get; set; } = "";
    public double Probability { get; set; }
    public double Confidence { get; set; }
    public string Model { get; set; } = "";
    public string? UsageJson { get; set; }
    public string PromptVersion { get; set; } = "";
    public int WaitCount { get; set; }
    public int Attempts { get; set; }
    public DateTime? RetryAt { get; set; }
    public DateTime ChangedAt { get; set; }
    public bool FlushRequested { get; set; }
    public bool GenerationRequested { get; set; }
    public int FlushVersion { get; set; }
    public int EvaluatedFlushVersion { get; set; }
}

public partial class PlaybackStore
{
    public async Task<object> SaveNote(string id, string markdown, string author, int? basedOnVersion = null)
    {
        if (markdown.Length > 250_000) throw new InvalidOperationException("Note is too large");
        var session = await Session(id) ?? throw new InvalidOperationException("Session not found");
        if (basedOnVersion.HasValue && basedOnVersion != session.NoteVersion)
            throw new InvalidOperationException("Notes changed; keep your editor text and compare with the latest version");
        var previous = session.CurrentNote;
        var citations = NoteSections.CloneCitations(previous?.Citations ?? []);
        var deleted = previous?.DeletedSectionIds.ToList() ?? [];
        var suppressed = previous?.SuppressedSourceIds.ToList() ?? [];
        var allowed = MaterialSources.Allowed(session);
        var sections = NoteSections.UserEdit(previous, id, markdown, allowed, citations, deleted, suppressed);
        return await SaveSectionNote(id, new SectionUpdate(sections, citations, []),
            ContentHash.Of($"user:{session.NoteVersion}:{markdown}"), session.NoteVersion, session.NoteLanguage,
            author, deleted, suppressed);
    }
    public async Task<List<Note>> NoteHistory(string id)
    {
        var latest = await Collection<Note>("notes").Find(x => x.SessionId == id).SortByDescending(x => x.Version).Limit(100).ToListAsync();
        latest.Reverse();
        return latest;
    }
    public virtual async Task MarkNotes(IEnumerable<string> ids, string status, string? error = null)
    {
        var list = ids.ToArray();
        if (list.Length == 0) return;
        var retryAt = status == "failed"
            ? DateTime.UtcNow.AddMinutes(1)
            : status == "processing" ? DateTime.UtcNow.AddMinutes(5) : (DateTime?)null;
        var update = Builders<Transcript>.Update
            .Set(x => x.NoteStatus, status)
            .Set(x => x.NoteError, error)
            .Set(x => x.NoteRetryAt, retryAt);
        if (status == "failed") update = update.Inc(x => x.NoteAttempts, 1);
        await Collection<Transcript>("transcripts").UpdateManyAsync(x => list.Contains(x.Id), update);
        foreach (var group in (await Collection<Transcript>("transcripts").Find(x => list.Contains(x.Id))
            .Project(x => new { x.Id, x.SessionId }).ToListAsync()).GroupBy(x => x.SessionId))
            Touch(group.Key, "transcript", group.Select(x => x.Id));
    }
    public virtual async Task<List<string>> SessionsWithPendingNotes()
    {
        var ids = await Collection<Transcript>("transcripts").Find(x => x.Original != "" &&
            (x.NoteStatus == "pending" || x.NoteStatus == "deferred" || x.NoteStatus == "failed" || x.NoteStatus == "processing"))
            .Project(x => x.SessionId).ToListAsync();
        var materialSessions = await Collection<Material>("materials").Find(FilterDefinition<Material>.Empty).Project(x => x.SessionId).ToListAsync();
        var flushed = await Collection<NoteGateRecord>("note_gates").Find(x => x.FlushRequested).Project(x => x.SessionId).ToListAsync();
        return ids.Concat(materialSessions).Concat(flushed).Distinct().ToList();
    }
    public virtual async Task<List<string>> SessionsNeedingOrganization() {
        var latest = await Collection<Note>("notes").Find(x => x.Sections.Count > 0).SortByDescending(x => x.CreatedAt).Limit(128).ToListAsync();
        return latest.GroupBy(x => x.SessionId).Where(x => x.First().Sections.Any(NoteAgent.ShouldOrganize)).Take(16).Select(x => x.Key).ToList();
    }
    public async Task<Note?> NoteVersion(string id, int version) => await Collection<Note>("notes")
        .Find(x => x.SessionId == id && x.Version == version).FirstOrDefaultAsync();
    public async Task<object> NotePage(string id, int? before, int limit = 20) {
        if (limit is < 1 or > 50 || before is < 1) throw new InvalidOperationException("Invalid note history page");
        var notes = await Collection<Note>("notes").Find(x => x.SessionId == id && (before == null || x.Version < before))
            .SortByDescending(x => x.Version).Limit(limit + 1).ToListAsync();
        return new { items = notes.Take(limit).Select(x => new { x.Version, x.BasedOnVersion, x.Author, x.CreatedAt,
            sectionCount = x.Sections.Count, x.InputHash }), nextBefore = notes.Count > limit ? (int?)notes[limit - 1].Version : null };
    }
    public virtual async Task<NoteGateRecord?> NoteGate(string id) => await Collection<NoteGateRecord>("note_gates").Find(x => x.SessionId == id).FirstOrDefaultAsync();
    public virtual Task SaveNoteGate(NoteGateRecord item) {
        item.Id = item.SessionId;
        // Stop may arrive while a provider request is running. Updating decision fields must not clear its flush flag.
        return Collection<NoteGateRecord>("note_gates").UpdateOneAsync(x => x.Id == item.Id,
            Builders<NoteGateRecord>.Update.Set(x => x.SessionId, item.SessionId).Set(x => x.InputHash, item.InputHash)
                .Set(x => x.Status, item.Status).Set(x => x.Decision, item.Decision).Set(x => x.Probability, item.Probability)
                .Set(x => x.Confidence, item.Confidence).Set(x => x.Model, item.Model).Set(x => x.UsageJson, item.UsageJson)
                .Set(x => x.PromptVersion, item.PromptVersion).Set(x => x.WaitCount, item.WaitCount)
                .Set(x => x.Attempts, item.Attempts).Set(x => x.RetryAt, item.RetryAt).Set(x => x.ChangedAt, item.ChangedAt)
                .Set(x => x.GenerationRequested, item.GenerationRequested).Set(x => x.EvaluatedFlushVersion, item.EvaluatedFlushVersion),
            new UpdateOptions { IsUpsert = true });
    }
    public async Task RequestNoteFlush(string id) {
        await Collection<NoteGateRecord>("note_gates").UpdateOneAsync(x => x.Id == id,
            Builders<NoteGateRecord>.Update.Set(x => x.SessionId, id).Set(x => x.FlushRequested, true).Inc(x => x.FlushVersion, 1), new UpdateOptions { IsUpsert = true });
    }
    public virtual Task AcknowledgeNoteFlush(string id, int version) => Collection<NoteGateRecord>("note_gates").UpdateOneAsync(
        x => x.Id == id && x.FlushVersion == version, Builders<NoteGateRecord>.Update.Set(x => x.FlushRequested, false));
    // Saves one new note version from a section update. Citations become persistent IDs, an AI author must
    // keep every saved point, and the base version and note language must not have changed in the meantime.
    public virtual async Task<object> SaveSectionNote(string id, SectionUpdate update, string inputHash, int basedOnVersion, string language,
        string author = "agent", List<string>? deleted = null, List<string>? suppressed = null)
    {
        update = update with { Citations = NoteSections.CloneCitations(update.Citations) };
        var allowed = update.Sections.SelectMany(s => s.Points).SelectMany(p => p.SourceIds)
            .Concat(update.Citations.SelectMany(c => c.SourceIds))
            .Distinct()
            .ToList();
        update = update with
        {
            Sections = update.Sections.Select(section => new NoteSection
            {
                Id = section.Id,
                Version = section.Version,
                Title = section.Title,
                UserEdited = section.UserEdited,
                OrganizedAt = section.OrganizedAt,
                OrganizedVersion = section.OrganizedVersion,
                Markdown = NoteSections.Compact(id, section.Markdown, allowed, update.Citations),
                Points = section.Points.Select(point => new NotePoint
                {
                    Id = point.Id,
                    SourceIds = point.SourceIds.ToList(),
                    Text = NoteSections.Compact(id, point.Text, allowed, update.Citations)
                }).ToList()
            }).ToList()
        };
        var markdown = NoteSections.Render(update.Sections);
        if (markdown.Length > 250_000) throw new InvalidOperationException("Note exceeds the document size limit");
        var writeLock = noteWriteLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await writeLock.WaitAsync();
        try
        {
            var settings = await SessionSettings(id);
            var previous = await Collection<Note>("notes").Find(x => x.SessionId == id).SortByDescending(x => x.Version).FirstOrDefaultAsync();
            if (previous?.InputHash == inputHash) return new { previous.Version, previous.Markdown, previous.Author };
            if ((previous?.Version ?? 0) != basedOnVersion || settings.NoteLanguage != language)
                throw new InvalidOperationException("Note or language changed during this operation; the result was not applied");
            if (author.StartsWith("agent")) NoteSections.RequireRetention(previous, update, allowed, allowProtectedReformat: author == "agent organization");
            var coverage = (previous?.Coverage ?? []).Where(x => !update.Coverage.Any(y => y.SourceId == x.SourceId)).Concat(update.Coverage).ToList();
            foreach (var item in coverage.Where(x => x.Status == "covered"))
            {
                item.PointIds = update.Sections.SelectMany(x => x.Points).Where(x => x.SourceIds.Contains(item.SourceId)).Select(x => x.Id).ToList();
                if (item.PointIds.Count == 0)
                {
                    item.Status = "suppressed";
                    item.Reason = "Written point removed by an explicit user edit/restore; do not regenerate automatically";
                }
            }
            var usedCitations = NoteSections.UsedCitations(update.Sections, update.Citations);
            var note = new Note
            {
                Id = Guid.NewGuid().ToString("N"),
                SessionId = id,
                Version = basedOnVersion + 1,
                BasedOnVersion = basedOnVersion,
                Markdown = markdown,
                Author = author,
                OutputLanguage = language,
                CreatedAt = DateTime.UtcNow,
                Sections = update.Sections,
                Citations = usedCitations,
                InputHash = inputHash,
                Coverage = coverage,
                DeletedSectionIds = (deleted ?? previous?.DeletedSectionIds ?? []).Distinct().ToList(),
                SuppressedSourceIds = (suppressed ?? previous?.SuppressedSourceIds ?? []).Distinct().ToList(),
                TranscriptIds = update.Sections.SelectMany(x => x.Points).SelectMany(x => x.SourceIds)
                    .Concat(usedCitations.SelectMany(x => x.SourceIds)).Distinct().ToList(),
                MaterialIds = previous?.MaterialIds.ToList() ?? [],
                InputTranscriptIds = update.Coverage.Select(x => x.SourceId).ToList(),
                Edits = NoteChangeLog.Build(previous?.Markdown ?? "", markdown, update.Citations.SelectMany(x => x.SourceIds), []),
                SourceFrom = previous?.SourceFrom,
                SourceThrough = previous?.SourceThrough,
                ProcessedThroughMs = previous?.ProcessedThroughMs ?? 0
            };
            // Source membership is evidence linkage, not a claim of semantic coverage.
            var materialIds = await Collection<Material>("materials").Find(x => x.SessionId == id).Project(x => x.Id).ToListAsync();
            note.MaterialIds = note.TranscriptIds.Select(MaterialSources.Parent).Intersect(materialIds).Distinct().ToList();
            note.TranscriptIds = note.TranscriptIds.Where(x => !materialIds.Contains(MaterialSources.Parent(x))).ToList();
            note.InputMaterialIds = update.Coverage.Select(x => MaterialSources.Parent(x.SourceId)).Intersect(materialIds).Distinct().ToList();
            note.InputTranscriptIds = note.InputTranscriptIds.Where(x => !materialIds.Contains(MaterialSources.Parent(x))).ToList();
            var linked = await Collection<Transcript>("transcripts").Find(x => x.SessionId == id && note.TranscriptIds.Contains(x.Id)).ToListAsync();
            if (linked.Count > 0)
            {
                note.ProcessedThroughMs = linked.Max(x => x.EndMs);
                note.SourceFrom = linked.Min(x => x.RecordedAt);
                note.SourceThrough = linked.Max(x => x.RecordedAt?.AddMilliseconds(x.EndMs - x.StartMs));
            }
            await Collection<Note>("notes").InsertOneAsync(note);
            Touch(id, "note");
            return new { note.Version, note.Markdown, note.Author };
        }
        finally { writeLock.Release(); }
    }
    public async Task<RecoveryPreview> Recovery(string id, int version) {
        var session = await Session(id) ?? throw new InvalidOperationException("Session not found");
        var historical = await NoteVersion(id, version) ?? throw new InvalidOperationException("Historical note not found");
        return NoteSections.Recover(historical, session.CurrentNote, MaterialSources.Allowed(session));
    }
    public async Task<object> RestoreNote(string id, int version, int? basedOnVersion) {
        var session = await Session(id) ?? throw new InvalidOperationException("Session not found");
        if (basedOnVersion is not null && session.NoteVersion != basedOnVersion) throw new InvalidOperationException("Notes changed; reload before restoring");
        var historical = await NoteVersion(id, version) ?? throw new InvalidOperationException("Historical note not found");
        var citations = NoteSections.CloneCitations(session.CurrentNote?.Citations ?? []);
        foreach (var citation in historical.Citations.Where(x => citations.All(c => c.Id != x.Id))) citations.Add(citation);
        var deleted = session.CurrentNote?.DeletedSectionIds.ToList() ?? [];
        var suppressed = session.CurrentNote?.SuppressedSourceIds.ToList() ?? [];
        var sections = NoteSections.UserEdit(session.CurrentNote, id, historical.Markdown, MaterialSources.Allowed(session), citations, deleted, suppressed);
        foreach (var section in sections) section.UserEdited = true;
        suppressed = suppressed.Except(sections.SelectMany(s => s.Points).SelectMany(p => p.SourceIds)).ToList();
        return await SaveSectionNote(id, new SectionUpdate(sections, citations, []), ContentHash.Of($"restore:{version}:{session.NoteVersion}"),
            session.NoteVersion, session.NoteLanguage, "user restore", deleted, suppressed);
    }
    public async Task<object> ApplyRecovery(string id, int version, int basedOnVersion, List<string> selected) {
        var session = await Session(id) ?? throw new InvalidOperationException("Session not found");
        if (session.NoteVersion != basedOnVersion) throw new InvalidOperationException("Notes changed; reload the recovery preview");
        var preview = await Recovery(id, version);
        var additions = preview.Candidates.Where(x => selected.Contains(x.Id)).ToList();
        if (selected.Count is < 1 or > 20 || additions.Count != selected.Distinct().Count() || additions.Any(x => x.Blocked))
            throw new InvalidOperationException("Invalid or intentionally deleted recovery selection");
        var sections = preview.CurrentSections.Concat(additions.Select(x => new NoteSection { Id = x.Id, Title = x.Title,
            Markdown = x.Markdown, Points = x.Points, UserEdited = true })).ToList();
        return await SaveSectionNote(id, new SectionUpdate(sections, preview.Citations, []),
            ContentHash.Of($"recover:{version}:{basedOnVersion}:" + string.Join(',', selected.Order())), basedOnVersion,
            session.NoteLanguage, "user recovery");
    }
}
