using System.Collections.Concurrent;
using MongoDB.Driver;
using Playback.Application.Notes;
using Playback.Domain.Notes;

namespace Playback.Infrastructure.Notes;

// Existing history stays intact; a single atomic head and durable history marker protect competing writers.
public sealed class MongoNoteRepository(IMongoCollection<Note> notes, IMongoCollection<NoteGateRecord> gates,
    INoteEvidence evidence, INoteSettings settings, ConcurrentDictionary<string, SemaphoreSlim> writeLocks,
    Action<string> changed, INoteHistoryStorage historyStorage, Func<string, Task<Dictionary<string, string>>>? supersessions = null) : INoteRepository
{
    readonly NoteHistoryJournal journal = new(historyStorage);
    Task<NoteHead> Head(string id) => journal.Read(id, () => notes.Find(x => x.SessionId == id).SortByDescending(x => x.Version).FirstOrDefaultAsync()!);
    public async Task<Note?> Latest(string id) => (await Head(id)).Current;
    public async Task<List<Note>> History(string id)
    {
        await Head(id);
        var result = await notes.Find(x => x.SessionId == id).SortByDescending(x => x.Version).Limit(100).ToListAsync();
        result.Reverse();
        return result;
    }
    public async Task<Note?> Version(string id, int version) {await Head(id); return await notes.Find(x => x.SessionId == id && x.Version == version).FirstOrDefaultAsync();}
    public async Task<object> Page(string id, int? before, int limit)
    {
        if (limit is < 1 or > 50 || before is < 1) throw new InvalidOperationException("Invalid note history page");
        await Head(id);
        var result = await notes.Find(x => x.SessionId == id && (before == null || x.Version < before))
            .SortByDescending(x => x.Version).Limit(limit + 1).ToListAsync();
        return new { items = result.Take(limit).Select(x => new { x.Version, x.BasedOnVersion, x.Author, x.CreatedAt,
            sectionCount = x.Sections.Count, x.InputHash }), nextBefore = result.Count > limit ? (int?)result[limit - 1].Version : null };
    }
    public Task<NoteGateRecord?> Gate(string id) => gates.Find(x => x.SessionId == id).FirstOrDefaultAsync()!;
    public Task RequestFlush(string id) => gates.UpdateOneAsync(x => x.Id == id,
        Builders<NoteGateRecord>.Update.Set(x => x.SessionId, id).Set(x => x.FlushRequested, true).Inc(x => x.FlushVersion, 1), new UpdateOptions { IsUpsert = true });
    public Task AcknowledgeFlush(string id, int version) => gates.UpdateOneAsync(x => x.Id == id && x.FlushVersion == version,
        Builders<NoteGateRecord>.Update.Set(x => x.FlushRequested, false));

    public async Task<object> Save(string id, SectionUpdate update, string inputHash, int basedOnVersion, string language, string author = "agent")
    {
        update = NoteMutation.Prepare(id, update);
        var writeLock = writeLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await writeLock.WaitAsync();
        try
        {
            var currentLanguage = await settings.Language(id);
            var previous = (await Head(id)).Current;
            if (previous?.InputHash == inputHash) return new { previous.Version, previous.Markdown, previous.Author };
            // The change log compares with the previous version as every reader sees it: through lineage.
            if (previous is not null && supersessions is not null) Playback.Api.Sources.Lineage.Apply(previous, await supersessions(id));
            var note = NoteMutation.Create(id, previous, update, inputHash, basedOnVersion, language, currentLanguage, author);
            var materialIds = await evidence.MaterialIds(id);
            note.MaterialIds = note.TranscriptIds.Select(MaterialSources.Parent).Intersect(materialIds).Distinct().ToList();
            note.TranscriptIds = note.TranscriptIds.Where(x => !materialIds.Contains(MaterialSources.Parent(x))).ToList();
            await journal.Commit(note, basedOnVersion);
            changed(id);
            return new { note.Version, note.Markdown, note.Author };
        }
        finally { writeLock.Release(); }
    }
}
