using System.Collections.Concurrent;
using System.Text.Json;
using Playback.Application.Notes;
using Playback.Domain.Assistance;
using Playback.Domain.Evidence;
using Playback.Domain.Models;
using Playback.Infrastructure.Storage;

// PlaybackStore with the note, activity and sync reads kept in memory, for checks without MongoDB.
// Every read returns a copy, as a database would.
sealed class MemoryStore() : PlaybackStore(inMemory: true)
{
    public readonly ConcurrentDictionary<string, List<Transcript>> Transcripts = new();
    public readonly ConcurrentDictionary<string, Note> Notes = new();
    public readonly ConcurrentDictionary<string, List<Note>> Versions = new();
    public readonly ConcurrentDictionary<string, string> Languages = new();
    public readonly ConcurrentDictionary<string, List<Material>> Materials = new();
    public readonly ConcurrentDictionary<string, List<ChunkRecord>> Chunks = new();
    public readonly ConcurrentDictionary<string, NoteGateRecord> Gates = new();
    public readonly ConcurrentDictionary<string, ActivityRecord> Activity = new();
    public readonly ConcurrentDictionary<string, List<TermCandidate>> Terms = new();
    public readonly ConcurrentDictionary<string, List<TermInsight>> Insights = new();
    public readonly List<string[]> SyncReadIds = [];
    public TimeProvider? Clock;
    public void Add(string id)
    {
        Transcripts[id] = [];
        Languages[id] = "zh-Hant";
        Materials[id] = [];
        Chunks[id] = [];
        Terms[id] = [];
        Insights[id] = [];
        Versions[id] = [];
    }

    // Confirmed speech in 8-second steps, one source per session.
    public void Speech(string id, string key, string text) => Transcripts[id].Add(new Transcript
    {
        Id = key,
        SessionId = id,
        SourceId = id,
        Original = text,
        StartMs = Transcripts[id].Count * 8000,
        EndMs = (Transcripts[id].Count + 1) * 8000
    });

    public override Task<SessionView?> Session(string id)
    {
        if (!Languages.ContainsKey(id)) return Task.FromResult<SessionView?>(null);
        Notes.TryGetValue(id, out var note);
        var supersessions = Lineage.Supersessions(Transcripts[id]);
        var copy = note is null ? null : Lineage.Apply(Copy(note), supersessions);
        var insights = Copy(Insights[id]);
        foreach (var insight in insights) insight.TranscriptIds = Lineage.Remap(insight.TranscriptIds, supersessions);
        return Task.FromResult<SessionView?>(new(id, id, null, DateTime.UtcNow.Date, copy?.Markdown ?? "", copy?.Version ?? 0, false, "zh-Hant",
            Copy(Materials[id]), Copy(Transcripts[id].Where(x => x.Active).ToList()), Copy(Chunks[id]), Copy(Terms[id]), insights, copy, NoteLanguage: Languages[id])
            { Supersessions = supersessions });
    }
    public override Task<Dictionary<string, string>> Supersessions(string sessionId) =>
        Task.FromResult(Lineage.Supersessions(Transcripts[sessionId]));
    public override Task<List<Transcript>> AllTranscripts(string sessionId) =>
        Task.FromResult(Copy(Transcripts[sessionId].OrderBy(x => x.StartMs).ToList()));

    public override Task<List<SyncRecord>> ReadSyncRecords(string id, string kind, string[]? ids)
    {
        SyncReadIds.Add(ids ?? []);
        return Task.FromResult(kind switch
        {
            "transcript" => Transcripts[id].Where(x => x.Active && (ids == null || ids.Contains(x.Id))).Select(x => new SyncRecord(kind, x.Id, Copy(x))).ToList(),
            "lineage" => [new SyncRecord(kind, id, new { supersededBy = Lineage.Supersessions(Transcripts[id]) })],
            "material" => Materials[id].Where(x => ids == null || ids.Contains(x.Id)).Select(x => new SyncRecord(kind, x.Id, Copy(x))).ToList(),
            "insight" => Insights[id].Where(x => ids == null || ids.Contains(x.Id)).Select(x => new SyncRecord(kind, x.Id, Copy(x))).ToList(),
            _ => new List<SyncRecord>()
        });
    }

    public override Task<TermCandidate?> ReadSyncTerm(string id, string term) => Task.FromResult(Terms[id].FirstOrDefault(x => x.Text == term));

    public override Task<NoteGateRecord?> NoteGate(string id) => Task.FromResult(Gates.TryGetValue(id, out var gate) ? Copy(gate) : null);

    public override Task AcknowledgeNoteFlush(string id, int version)
    {
        if (Gates.TryGetValue(id, out var gate) && gate.FlushVersion == version) gate.FlushRequested = false;
        return Task.CompletedTask;
    }
    public override Task RequestNoteFlush(string id)
    {
        var gate = Gates.GetOrAdd(id, _ => new() { SessionId = id });
        gate.FlushRequested = true;
        gate.FlushVersion++;
        return Task.CompletedTask;
    }

    public override Task SaveActivity(ActivityRecord item)
    {
        Activity[item.Id] = Copy(item);
        return Task.CompletedTask;
    }

    public override Task<List<ActivityRecord>> ActivityHistory(string id) =>
        Task.FromResult(Activity.Values.Where(x => x.SessionId == id).Select(Copy).ToList());

    public override Task MarkNotes(IEnumerable<string> ids, string status, string? error = null)
    {
        foreach (var transcript in Transcripts.Values.SelectMany(x => x).Where(x => ids.Contains(x.Id))) transcript.NoteStatus = status;
        return Task.CompletedTask;
    }

    public override Task<List<Note>> NoteHistory(string id) => Task.FromResult(Copy(Versions[id].TakeLast(100).ToList()));
    public override Task<Note?> NoteVersion(string id, int version) =>
        Task.FromResult(Versions[id].FirstOrDefault(x => x.Version == version) is { } note ? Copy(note) : null);
    public override Task<List<string>> NoteMaterialIds(string id) => Task.FromResult(Materials[id].Select(x => x.Id).ToList());

    // Storage is in memory; the mutation rules are the production domain implementation.
    public override Task<object> SaveSectionNote(string id, SectionUpdate update, string hash, int basedOnVersion, string language,
        string author = "agent")
    {
        Notes.TryGetValue(id, out var previous);
        if (previous?.InputHash == hash) return Task.FromResult<object>(new { previous.Version, previous.Markdown, previous.Author });
        var note = NoteMutation.Create(id, previous is null ? null : Lineage.Apply(Copy(previous), Lineage.Supersessions(Transcripts[id])), NoteMutation.Prepare(id, update), hash,
            basedOnVersion, language, Languages[id], author, (Clock ?? TimeProvider.System).GetUtcNow().UtcDateTime);
        Notes[id] = Copy(note);
        Versions[id].Add(Copy(note));
        return Task.FromResult<object>(new { note.Version, note.Markdown, note.Author });
    }

    static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}

