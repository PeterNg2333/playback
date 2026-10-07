using System.Collections.Concurrent;
using System.Text.Json;
using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Api.Terms;
using Playback.Application.Notes;

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
    public readonly MemoryProcessingRepository ProcessingRepository = new();
    public readonly Dictionary<string, Playback.Domain.Audio.SpeechBoundary> Boundaries = new();
    public override Task<Playback.Domain.Audio.SpeechBoundary?> LatestSpeechBoundary(string id) => Task.FromResult(Boundaries.GetValueOrDefault(id));
    ProcessingSettings Processing => new(ProcessingRepository, Playback.Domain.Notes.ProcessingPolicy.Defaults);
    public override Task<ProcessingSettingsView> WorkspaceProcessingSettings() => Processing.Workspace("local");
    public override Task<ProcessingSettingsView> SessionProcessingSettings(string id) => Languages.ContainsKey(id)
        ? Processing.Session("local", id) : throw new InvalidOperationException("Session not found");
    public override async Task<ProcessingSettingsView> SaveWorkspaceProcessingSettings(ProcessingSettingsUpdate input)
    {
        var result = await Processing.SaveWorkspace("local", input);
        foreach (var id in Languages.Keys) Touch(id, "processing");
        return result;
    }
    public override async Task<ProcessingSettingsView> SaveSessionProcessingSettings(string id, ProcessingSettingsUpdate input)
    {
        if (!Languages.ContainsKey(id)) throw new InvalidOperationException("Session not found");
        var result = await Processing.SaveSession("local", id, input);
        Touch(id, "processing");
        return result;
    }

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
        var copy = note is null ? null : Copy(note);
        return Task.FromResult<SessionView?>(new(id, id, null, DateTime.UtcNow.Date, copy?.Markdown ?? "", copy?.Version ?? 0, 0, false, "zh-Hant",
            Copy(Materials[id]), Copy(Transcripts[id]), Copy(Chunks[id]), Copy(Terms[id]), Copy(Insights[id]), copy, NoteLanguage: Languages[id]));
    }

    public override async Task<List<SyncRecord>> ReadSyncRecords(string id, string kind, string[]? ids)
    {
        SyncReadIds.Add(ids ?? []);
        return kind switch
        {
            "processing" => [new SyncRecord(kind, id, new { processingSettings = await SessionProcessingSettings(id), noteAdmission = NoteAdmissionView.Read(await NoteGate(id)) })],
            "transcript" => Transcripts[id].Where(x => ids == null || ids.Contains(x.Id)).Select(x => new SyncRecord(kind, x.Id, Copy(x))).ToList(),
            "material" => Materials[id].Where(x => ids == null || ids.Contains(x.Id)).Select(x => new SyncRecord(kind, x.Id, Copy(x))).ToList(),
            "insight" => Insights[id].Where(x => ids == null || ids.Contains(x.Id)).Select(x => new SyncRecord(kind, x.Id, Copy(x))).ToList(),
            _ => new List<SyncRecord>()
        };
    }

    public override Task<TermCandidate?> ReadSyncTerm(string id, string term) => Task.FromResult(Terms[id].FirstOrDefault(x => x.Text == term));

    public override Task<NoteGateRecord?> NoteGate(string id) => Task.FromResult(Gates.TryGetValue(id, out var gate) ? Copy(gate) : null);

    public override Task SaveNoteGate(NoteGateRecord item)
    {
        var copy = Copy(item);
        if (Gates.TryGetValue(item.SessionId, out var latest))
        {
            copy.FlushRequested = latest.FlushRequested;
            copy.FlushVersion = latest.FlushVersion;
        }
        Gates[item.SessionId] = copy;
        Touch(item.SessionId, "processing");
        return Task.CompletedTask;
    }

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
        Touch(id, "processing");
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
    public override Task<NoteProvenance> NoteProvenance(string id, IReadOnlyCollection<string> sourceIds) => Task.FromResult(new NoteProvenance(
        Materials[id].Select(x => x.Id).ToList(), Transcripts[id].Where(x => sourceIds.Contains(x.Id))
            .Select(x => new NoteSourceTiming(x.StartMs, x.EndMs, x.RecordedAt)).ToList()));

    // Storage is in memory; the mutation rules are the production domain implementation.
    public override Task<object> SaveSectionNote(string id, SectionUpdate update, string hash, int basedOnVersion, string language,
        string author = "agent", List<string>? deleted = null, List<string>? suppressed = null)
    {
        Notes.TryGetValue(id, out var previous);
        if (previous?.InputHash == hash) return Task.FromResult<object>(new { previous.Version, previous.Markdown, previous.Author });
        var note = NoteMutation.Create(id, previous is null ? null : Copy(previous), NoteMutation.Prepare(id, update), hash,
            basedOnVersion, language, Languages[id], author, deleted, suppressed);
        Notes[id] = Copy(note);
        Versions[id].Add(Copy(note));
        return Task.FromResult<object>(new { note.Version, note.Markdown, note.Author });
    }

    static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}

sealed class MemoryProcessingRepository : IProcessingSettingsRepository
{
    readonly Dictionary<string, ProcessingSettingsRecord> records = new();
    public Task<ProcessingSettingsRecord?> Read(string key)
    {
        lock (records) return Task.FromResult(records.TryGetValue(key, out var value) ? Copy(value) : null);
    }
    public Task Save(ProcessingSettingsRecord value, int expectedRevision)
    {
        lock (records)
        {
            if ((records.GetValueOrDefault(value.Id)?.Revision ?? 0) != expectedRevision)
                throw new InvalidOperationException("Processing settings changed; reload before saving");
            records[value.Id] = Copy(value);
        }
        return Task.CompletedTask;
    }
    static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
