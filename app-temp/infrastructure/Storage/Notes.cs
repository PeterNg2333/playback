using MongoDB.Bson;
using MongoDB.Driver;
using Playback.Infrastructure.Notes;
using Playback.Application.Notes;
using Playback.Domain.Models;

namespace Playback.Infrastructure.Storage;

// Compatibility adapter for existing callers; note storage is in Infrastructure and use cases in Application.
public partial class PlaybackStore
{
    MongoNoteRepository? noteRepository;
    MongoNoteRepository NoteRepository => noteRepository ??= new(
        new MongoNoteHistoryStorage(Collection<BsonDocument>("note_heads"), Collection<BsonDocument>("notes"), Collection<BsonDocument>("note_parts")),
        Collection<NoteGateRecord>("note_gates"), new NoteStoreAdapter(this), new NoteStoreAdapter(this), noteWriteLocks, id => Touch(id, "note"), Supersessions);
    NoteCommands NoteCommands => new(new NoteStoreAdapter(this), new NoteStoreAdapter(this));
    public Task<object> SaveNote(string id, string markdown, string author, int? basedOnVersion = null) => NoteCommands.SaveNote(id, markdown, author, basedOnVersion);
    public virtual Task<List<Note>> NoteHistory(string id) => NoteRepository.History(id);
    public virtual Task<Note?> NoteVersion(string id, int version) => NoteRepository.Version(id, version);
    public Task<object> NotePage(string id, int? before, int limit = 20) => NoteRepository.Page(id, before, limit);
    public virtual Task<NoteGateRecord?> NoteGate(string id) => NoteRepository.Gate(id);
    public virtual async Task RequestNoteFlush(string id) => await NoteRepository.RequestFlush(id);
    public virtual async Task AcknowledgeNoteFlush(string id, int version) => await NoteRepository.AcknowledgeFlush(id, version);
    public virtual Task<object> SaveSectionNote(string id, SectionUpdate update, string inputHash, int basedOnVersion, string language,
        string author = "agent") => NoteRepository.Save(id, update, inputHash, basedOnVersion, language, author);
    public Task<object> RestoreNote(string id, int version, int? basedOnVersion) => NoteCommands.RestoreNote(id, version, basedOnVersion);

    public virtual Task<List<string>> NoteMaterialIds(string id) =>
        Collection<Material>("materials").Find(x => x.SessionId == id).Project(x => x.Id).ToListAsync();

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
        var ids = await Collection<Transcript>("transcripts").Find(x => x.Original != "" && x.SupersededBy == null &&
            (x.NoteStatus == "pending" || x.NoteStatus == "deferred" || x.NoteStatus == "failed" || x.NoteStatus == "processing"))
            .Project(x => x.SessionId).ToListAsync();
        var materialSessions = await Collection<Material>("materials").Find(FilterDefinition<Material>.Empty).Project(x => x.SessionId).ToListAsync();
        var flushed = await Collection<NoteGateRecord>("note_gates").Find(x => x.FlushRequested).Project(x => x.SessionId).ToListAsync();
        return ids.Concat(materialSessions).Concat(flushed).Distinct().ToList();
    }

}
