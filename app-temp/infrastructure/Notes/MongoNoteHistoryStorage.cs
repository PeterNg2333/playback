using MongoDB.Driver;
using Playback.Application.Notes;
using Playback.Domain.Notes;
namespace Playback.Infrastructure.Notes;
public sealed class MongoNoteHistoryStorage(IMongoCollection<NoteHead> heads, IMongoCollection<Note> history) : INoteHistoryStorage
{
    public Task<NoteHead?> Head(string id) => heads.Find(x => x.Id == id).FirstOrDefaultAsync()!;
    public async Task Initialize(NoteHead seed)
    {
        try {await heads.InsertOneAsync(seed);}
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { }
    }
    public async Task<bool> Advance(NoteHead next, int expectedVersion) => (await heads.ReplaceOneAsync(x => x.Id == next.Id && x.Version == expectedVersion && !x.PendingHistory, next)).MatchedCount == 1;
    public async Task AppendHistory(Note note)
    {
        var existing = await history.Find(x => x.SessionId == note.SessionId && x.Version == note.Version).FirstOrDefaultAsync();
        if (existing is not null)
        {
            if (existing.InputHash != note.InputHash) throw new InvalidOperationException("A different history revision already exists; current head is retained for inspection");
            return;
        }
        // Deterministic identity makes an interrupted/competing history flush idempotent.
        note.Id = "note_" + Playback.Domain.ContentHash.Of(note.SessionId + ":" + note.Version);
        try {await history.InsertOneAsync(note);}
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            existing = await history.Find(x => x.Id == note.Id).FirstOrDefaultAsync();
            if (existing?.InputHash != note.InputHash) throw new InvalidOperationException("History identity conflict; current head is retained");
        }
    }
    public Task Acknowledge(string id, int version, string inputHash) => heads.UpdateOneAsync(x => x.Id == id && x.Version == version && x.Current!.InputHash == inputHash,
        Builders<NoteHead>.Update.Set(x => x.PendingHistory, false));
}
