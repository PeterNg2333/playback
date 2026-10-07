using Playback.Application.Notes;
using Playback.Domain.Notes;
using System.Text.Json;
static class NoteHistoryJournalChecks
{
    public static async Task Run()
    {
        var storage = new MemoryJournal(); var journal = new NoteHistoryJournal(storage);
        var legacy = new Note {SessionId = "s", Version = 7, InputHash = "old", Markdown = "Saved original"};
        await journal.Read("s", () => Task.FromResult<Note?>(legacy));
        var next = new Note {SessionId = "s", Version = 8, InputHash = "new", Markdown = "Retained original and new evidence"};
        storage.FailAppend = true;
        await Expect.RejectsAsync(() => journal.Commit(next, 7), "Simulated history failure was hidden");
        Expect.That(storage.Current!.Version == 8 && storage.Current.PendingHistory && storage.History.Count == 0, "History failure lost the authoritative pending head");
        storage.FailAppend = false; storage.FailAck = true;
        await Expect.RejectsAsync(() => new NoteHistoryJournal(storage).Read("s", () => Task.FromResult<Note?>(legacy)), "Simulated acknowledgement crash was hidden");
        Expect.That(storage.History.Count == 1 && storage.Current.PendingHistory, "Crash after append lost durable history intent");
        storage.FailAck = false;
        var recovered = await new NoteHistoryJournal(storage).Read("s", () => Task.FromResult<Note?>(legacy));
        Expect.That(recovered.Version == 8 && !storage.Current.PendingHistory && storage.History.Count == 1, "Restart duplicated or lost saved history");
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i => {try {await journal.Commit(new() {SessionId = "s", Version = 9, InputHash = "writer" + i}, 8); return true;} catch (InvalidOperationException) {return false;}}));
        Expect.That(results.Count(x => x) == 1 && storage.History.Count == 2 && storage.Current.Version == 9, "Competing writers both advanced one base revision");
        await Expect.RejectsAsync(() => journal.Commit(new() {SessionId = "s", Version = 8}, 7), "Stale writer advanced the recovered head");
        Console.WriteLine("Note journal checks passed in memory: legacy seed, failure after head commit/after history append, restart flush without duplicates, competing writer CAS and stale rejection");
    }
    sealed class MemoryJournal : INoteHistoryStorage
    {
        public NoteHead? Current; public readonly Dictionary<int, Note> History = []; public bool FailAppend, FailAck;
        static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
        public Task<NoteHead?> Head(string id) {lock (History) return Task.FromResult(Current is null ? null : Copy(Current));}
        public Task Initialize(NoteHead seed) {lock (History) Current ??= Copy(seed); return Task.CompletedTask;}
        public Task<bool> Advance(NoteHead next, int expectedVersion) {lock (History) {if (Current is null || Current.Version != expectedVersion || Current.PendingHistory) return Task.FromResult(false); Current = Copy(next); return Task.FromResult(true);}}
        public Task AppendHistory(Note note) {if (FailAppend) throw new InvalidOperationException("Simulated append failure"); lock (History) {if (History.TryGetValue(note.Version, out var previous) && previous.InputHash != note.InputHash) throw new InvalidOperationException("History collision"); History[note.Version] = Copy(note);} return Task.CompletedTask;}
        public Task Acknowledge(string id, int version, string inputHash) {if (FailAck) throw new InvalidOperationException("Simulated acknowledgement crash"); lock (History) if (Current?.Version == version && Current.Current?.InputHash == inputHash) Current.PendingHistory = false; return Task.CompletedTask;}
    }
}
