using System.Collections.Concurrent;
using System.Text.Json;
using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Api.Terms;

// PlaybackStore with the note, activity and sync reads kept in memory, for checks without MongoDB.
// Every read returns a copy, as a database would.
sealed class MemoryStore : PlaybackStore
{
    public readonly ConcurrentDictionary<string, List<Transcript>> Transcripts = new();
    public readonly ConcurrentDictionary<string, Note> Notes = new();
    public readonly ConcurrentDictionary<string, string> Languages = new();
    public readonly ConcurrentDictionary<string, List<Material>> Materials = new();
    public readonly ConcurrentDictionary<string, List<ChunkRecord>> Chunks = new();
    public readonly ConcurrentDictionary<string, NoteGateRecord> Gates = new();
    public readonly ConcurrentDictionary<string, ActivityRecord> Activity = new();
    public readonly ConcurrentDictionary<string, List<TermCandidate>> Terms = new();
    public readonly ConcurrentDictionary<string, List<TermInsight>> Insights = new();
    public readonly List<string[]> SyncReadIds = [];

    public void Add(string id)
    {
        Transcripts[id] = [];
        Languages[id] = "zh-Hant";
        Materials[id] = [];
        Chunks[id] = [];
        Terms[id] = [];
        Insights[id] = [];
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
        Notes.TryGetValue(id, out var note);
        var copy = note is null ? null : Copy(note);
        return Task.FromResult<SessionView?>(new(id, id, null, DateTime.UtcNow.Date, copy?.Markdown ?? "", copy?.Version ?? 0, 0, false, "zh-Hant",
            Copy(Materials[id]), Copy(Transcripts[id]), Copy(Chunks[id]), Copy(Terms[id]), Copy(Insights[id]), copy, NoteLanguage: Languages[id]));
    }

    public override Task<List<SyncRecord>> ReadSyncRecords(string id, string kind, string[]? ids)
    {
        SyncReadIds.Add(ids ?? []);
        return Task.FromResult(kind switch
        {
            "transcript" => Transcripts[id].Where(x => ids == null || ids.Contains(x.Id)).Select(x => new SyncRecord(kind, x.Id, Copy(x))).ToList(),
            "material" => Materials[id].Where(x => ids == null || ids.Contains(x.Id)).Select(x => new SyncRecord(kind, x.Id, Copy(x))).ToList(),
            "insight" => Insights[id].Where(x => ids == null || ids.Contains(x.Id)).Select(x => new SyncRecord(kind, x.Id, Copy(x))).ToList(),
            _ => new List<SyncRecord>()
        });
    }

    public override Task<TermCandidate?> ReadSyncTerm(string id, string term) => Task.FromResult(Terms[id].FirstOrDefault(x => x.Text == term));

    public override Task<NoteGateRecord?> NoteGate(string id) => Task.FromResult(Gates.TryGetValue(id, out var gate) ? Copy(gate) : null);

    public override Task SaveNoteGate(NoteGateRecord item)
    {
        Gates[item.SessionId] = Copy(item);
        return Task.CompletedTask;
    }

    public override Task AcknowledgeNoteFlush(string id, int version)
    {
        if (Gates.TryGetValue(id, out var gate) && gate.FlushVersion == version) gate.FlushRequested = false;
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

    // The same base, language and retention rules as the MongoDB store, without its source bookkeeping.
    public override Task<object> SaveSectionNote(string id, SectionUpdate update, string hash, int basedOnVersion, string language,
        string author = "agent", List<string>? deleted = null, List<string>? suppressed = null)
    {
        Notes.TryGetValue(id, out var previous);
        if ((previous?.Version ?? 0) != basedOnVersion || Languages[id] != language) throw new InvalidOperationException("Base changed during generation");
        if (author.StartsWith("agent"))
            NoteSections.RequireRetention(previous, update, update.Sections.SelectMany(x => x.Points).SelectMany(x => x.SourceIds),
                allowProtectedReformat: author == "agent organization");
        var coverage = (previous?.Coverage ?? []).Where(x => !update.Coverage.Any(y => y.SourceId == x.SourceId)).Concat(update.Coverage).ToList();
        var note = new Note
        {
            SessionId = id,
            Version = basedOnVersion + 1,
            BasedOnVersion = basedOnVersion,
            CreatedAt = DateTime.UtcNow,
            Markdown = NoteSections.Render(update.Sections),
            Sections = Copy(update.Sections),
            Citations = Copy(update.Citations),
            Coverage = Copy(coverage),
            InputHash = hash,
            Author = author,
            InputTranscriptIds = update.Coverage.Select(x => x.SourceId).ToList(),
            TranscriptIds = update.Sections.SelectMany(s => s.Points).SelectMany(p => p.SourceIds).Distinct().ToList(),
            SuppressedSourceIds = suppressed ?? previous?.SuppressedSourceIds.ToList() ?? [],
            DeletedSectionIds = deleted ?? previous?.DeletedSectionIds.ToList() ?? []
        };
        Notes[id] = note;
        return Task.FromResult<object>(new { note.Version, note.Markdown, note.Author });
    }

    static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
