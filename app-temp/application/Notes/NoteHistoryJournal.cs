using Playback.Domain.Notes;
namespace Playback.Application.Notes;
public interface INoteHistoryStorage
{
    Task<NoteHead?> Head(string id);
    Task Initialize(NoteHead seed);
    Task<bool> Advance(NoteHead next, int expectedVersion);
    Task AppendHistory(Note note);
    Task Acknowledge(string id, int version, string inputHash);
}
public sealed class NoteHistoryJournal(INoteHistoryStorage storage)
{
    public async Task<NoteHead> Read(string id, Func<Task<Note?>> legacy)
    {
        var head = await storage.Head(id);
        if (head is null)
        {
            var previous = await legacy();
            await storage.Initialize(new() {Id = id, Version = previous?.Version ?? 0, Current = previous});
            head = await storage.Head(id) ?? throw new InvalidOperationException("Note head initialization failed");
        }
        await Recover(head); return head;
    }
    public async Task Recover(NoteHead head)
    {
        if (!head.PendingHistory || head.Current is null) return;
        await storage.AppendHistory(head.Current);
        await storage.Acknowledge(head.Id, head.Version, head.Current.InputHash!);
        head.PendingHistory = false;
    }
    public async Task Commit(Note note, int expectedVersion)
    {
        if (note.Version != expectedVersion + 1) throw new InvalidOperationException("Invalid next note revision");
        note.Id = "note_" + Playback.Domain.ContentHash.Of(note.SessionId + ":" + note.Version);
        var next = new NoteHead {Id = note.SessionId, Version = note.Version, Current = note, PendingHistory = true};
        if (!await storage.Advance(next, expectedVersion)) throw new InvalidOperationException("Notes changed; reload before saving");
        await Recover(next);
    }
}
