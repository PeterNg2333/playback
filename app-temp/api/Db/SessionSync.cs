using System.Collections.Concurrent;
using System.Text.Json;
using Playback.Api.Terms;
using MongoDB.Driver;
using MongoDB.Bson;
using System.Text.RegularExpressions;

namespace Playback.Api.Db;

// The page's change feed for one session: a paged first read, then only the records that changed.
// The change journal and reader cursors live in memory; an API restart makes readers start over.
public partial class PlaybackStore
{
    const int MaxJournalEvents = 8192;
    const int MaxSyncReaders = 32;
    static readonly TimeSpan SyncReaderIdle = TimeSpan.FromMinutes(5);
    const int MaxRecordsPerPage = 128;
    const int MaxPageBytes = 400_000;
    // A record above 64 KB travels in 24,000-character fragments; one above 8 M characters is refused.
    const int FragmentAboveBytes = 64_000;
    const int FragmentChars = 24_000;
    const int MaxRecordChars = 8_000_000;
    const int MaxSyncedTerms = 100;

    sealed record SyncEvent(long Revision, string Kind, string[]? Ids);
    sealed class ChangeLog {
        public long Revision;
        public readonly Queue<SyncEvent> Events = new();
    }
    readonly ConcurrentDictionary<string, ChangeLog> syncLogs = new();
    readonly ConcurrentDictionary<string, SyncState> syncStates = new();
    public void Touch(string sessionId, string kind, IEnumerable<string>? ids = null) {
        var log = syncLogs.GetOrAdd(sessionId, _ => new ChangeLog());
        lock (log) {
            log.Events.Enqueue(new(++log.Revision, kind, ids?.Distinct().ToArray()));
            while (log.Events.Count > MaxJournalEvents) log.Events.Dequeue();
        }
    }
    public sealed record SyncRecord(string Kind, string Id, object Value);
    sealed record RecordFragment(string Kind, string Key, int Index, int Count, string Text);
    sealed class SyncState(string sessionId) {
        public readonly string SessionId = sessionId;
        public long Revision = -1;
        public DateTime LastUsed = DateTime.UtcNow;
        public readonly SemaphoreSlim Gate = new(1, 1);
        public Dictionary<string, string> Hashes = new();
        public Queue<(string Kind, string Key, object Value, string Hash)> Pending = new();
        public Dictionary<string, TermCandidate> Terms = new(StringComparer.OrdinalIgnoreCase);
        public string NoteLanguage = "zh-Hant";
        public int FullReads, RecordQueries;
    }
    static object SyncMeta(SessionRecord session, Note? note) => new {
        session.Id, session.Title, session.GroupId, session.CreatedAt,
        noteMarkdown = note?.Markdown ?? "", noteVersion = note?.Version ?? 0,
        session.TranslationEnabled, session.TranslationLanguage, session.AsrLanguage, session.AsrModel, session.NoteLanguage,
        currentNote = note is null ? null : new { note.Author, note.BasedOnVersion,
            note.InputHash, note.TranscriptIds, note.MaterialIds, note.InputTranscriptIds, note.InputMaterialIds,
            note.Edits, note.Sections, note.Citations, note.Coverage, note.SourceFrom, note.SourceThrough,
            note.DeletedSectionIds, note.SuppressedSourceIds }
    };
    // Only changed IDs are read after bootstrap. A null ID list is an explicit bulk operation,
    // such as changing the translation language; audio levels/idle polling never read a session.
    public virtual async Task<List<SyncRecord>> ReadSyncRecords(string id, string kind, string[]? ids) {
        var records = new List<SyncRecord>();
        if (kind == "meta") {
            var session = await SessionSettings(id);
            var note = await Collection<Note>("notes").Find(x => x.SessionId == id).SortByDescending(x => x.Version).FirstOrDefaultAsync();
            records.Add(new("meta", id, SyncMeta(session, note)));
        }
        if (kind == "transcript") {
            var session = await SessionSettings(id);
            var rows = await Collection<Transcript>("transcripts").Find(x => x.SessionId == id && (ids == null || ids.Contains(x.Id))).ToListAsync();
            foreach (var row in rows) { row.DisplayOriginal = LanguageSettings.CantoneseDisplay(row.Original, session.AsrLanguage, row.AsrDetectedLanguage);
                records.Add(new("transcript", row.Id, row)); }
        }
        if (kind == "chunk") foreach (var row in await Collection<ChunkRecord>("chunks").Find(x => x.SessionId == id && (ids == null || ids.Contains(x.Id))).ToListAsync()) records.Add(new(kind, row.Id, row));
        if (kind == "material") foreach (var row in await Collection<Material>("materials").Find(x => x.SessionId == id && (ids == null || ids.Contains(x.Id))).ToListAsync()) records.Add(new(kind, row.Id, row));
        if (kind == "insight") foreach (var row in await Collection<TermInsight>("term_insights").Find(x => x.SessionId == id && (ids == null || ids.Contains(x.Id))).ToListAsync()) records.Add(new(kind, row.Id, row));
        return records;
    }
    public virtual async Task<TermCandidate?> ReadSyncTerm(string id, string term) {
        var pattern = new BsonRegularExpression(Regex.Escape(term), "i");
        var tf = Builders<Transcript>.Filter.Eq(x => x.SessionId, id) & Builders<Transcript>.Filter.Ne(x => x.RecognitionStatus, "asr-empty") &
            Builders<Transcript>.Filter.Ne(x => x.Uncertain, true) & (Builders<Transcript>.Filter.Regex(x => x.Original, pattern) | Builders<Transcript>.Filter.Regex(x => x.DisplayOriginal, pattern));
        var ids = await Collection<Transcript>("transcripts").Find(tf).SortBy(x => x.StartMs).Project(x => x.Id).ToListAsync();
        var latest = await Collection<Transcript>("transcripts").Find(tf).SortByDescending(x => x.StartMs).FirstOrDefaultAsync();
        var mf = Builders<Material>.Filter.Eq(x => x.SessionId, id) & Builders<Material>.Filter.Regex(x => x.Text, pattern);
        var materialIds = await Collection<Material>("materials").Find(mf).Project(x => x.Id).ToListAsync();
        var material = latest is null ? await Collection<Material>("materials").Find(mf).FirstOrDefaultAsync() : null;
        if (ids.Count + materialIds.Count == 0) return null;
        if (latest is not null) latest.DisplayOriginal = LanguageSettings.CantoneseDisplay(latest.Original, (await SessionSettings(id)).AsrLanguage, latest.AsrDetectedLanguage);
        return new(term, ids, materialIds, TermCandidateExtractor.Context(term, latest?.SourceText ?? material?.Text ?? ""));
    }
    // Every page is bounded, the first read included. A cursor keeps fingerprints, not a second copy of the session.
    public async Task<object> SyncSession(string id, string? cursor) {
        var reset = cursor is not null && (!syncStates.TryGetValue(cursor, out var prior) || prior.SessionId != id);
        var key = reset || cursor is null ? Guid.NewGuid().ToString("N") : cursor;
        var state = syncStates.GetOrAdd(key, _ => new SyncState(id));
        await state.Gate.WaitAsync();
        try {
            state.LastUsed = DateTime.UtcNow;
            foreach (var old in syncStates.Where(x => x.Key != key && (x.Value.LastUsed < DateTime.UtcNow - SyncReaderIdle || syncStates.Count > MaxSyncReaders))
                .OrderBy(x => x.Value.LastUsed).Take(Math.Max(1, syncStates.Count - MaxSyncReaders)).ToList()) syncStates.TryRemove(old.Key, out _);
            var log = syncLogs.GetOrAdd(id, _ => new ChangeLog());
            long revision; List<SyncEvent> events;
            lock (log) {
                revision = log.Revision;
                if (state.Revision >= 0 && log.Events.TryPeek(out var first) && state.Revision < first.Revision - 1) {
                    state.Revision = -1; state.Pending.Clear(); state.Hashes.Clear(); reset = true;
                }
                events = log.Events.Where(x => x.Revision > state.Revision).ToList();
            }
            if (state.Pending.Count == 0 && state.Revision != revision) {
                void Add(string kind, string identity, object value) {
                    var identityKey = kind + ":" + identity;
                    var serialized = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)); var hash = ContentHash.Of(serialized);
                    if (state.Hashes.TryGetValue(identityKey, out var previous) && previous == hash) return;
                    if (serialized.Length > MaxRecordChars) throw new InvalidOperationException("Session record exceeds the sync assembly limit");
                    if (System.Text.Encoding.UTF8.GetByteCount(serialized) <= FragmentAboveBytes) state.Pending.Enqueue((kind, identityKey, value, hash));
                    else {
                        const int length = FragmentChars; var count = (serialized.Length + length - 1) / length;
                        for (var index = 0; index < count; index++) state.Pending.Enqueue(("fragment", identityKey,
                            new RecordFragment(kind, identityKey, index, count, serialized.Substring(index * length, Math.Min(length, serialized.Length - index * length))), hash));
                    }
                }
                void Remove(string identity) { if (state.Hashes.ContainsKey(identity)) state.Pending.Enqueue(("removed", identity, identity, "")); }
                void AddRecord(SyncRecord row) {
                    if (row.Value is TermInsight insight) {
                        var candidate = state.Terms.Values.FirstOrDefault(x => TermInsight.IdFor(id, x.Text, x.Context, state.NoteLanguage) == insight.Id);
                        if (candidate is not null) {
                            insight.TranscriptIds = insight.TranscriptIds.Concat(candidate.TranscriptIds).Distinct().ToList();
                            insight.MaterialIds = insight.MaterialIds.Concat(candidate.MaterialIds).Distinct().ToList();
                        }
                    }
                    Add(row.Kind, row.Id, row.Value);
                }
                if (state.Revision < 0 || events.Any(x => x.Kind == "display-settings")) {
                    // Bootstrap/reconfiguration is the only full read. Restarted/expired cursors bootstrap afresh.
                    var session = await Session(id) ?? throw new InvalidOperationException("Session not found"); state.FullReads++;
                    state.NoteLanguage = session.NoteLanguage;
                    Add("meta", id, SyncMeta(new SessionRecord { Id = id, Title = session.Title, GroupId = session.GroupId, CreatedAt = session.CreatedAt,
                        TranslationEnabled = session.TranslationEnabled, TranslationLanguage = session.TranslationLanguage,
                        AsrLanguage = session.AsrLanguage, AsrModel = session.AsrModel, NoteLanguage = session.NoteLanguage }, session.CurrentNote));
                    foreach (var item in session.Transcripts) Add("transcript", item.Id, item);
                    foreach (var item in session.Chunks) Add("chunk", item.Id, item);
                    foreach (var item in session.Materials) Add("material", item.Id, item);
                    foreach (var item in session.TermInsights) Add("insight", item.Id, item);
                    var current = new HashSet<string>(session.Transcripts.Select(x => "transcript:" + x.Id).Concat(session.Chunks.Select(x => "chunk:" + x.Id))
                        .Concat(session.Materials.Select(x => "material:" + x.Id)).Concat(session.TermInsights.Select(x => "insight:" + x.Id)).Concat(session.Terms.Select(x => "term:" + x.Text))) { "meta:" + id };
                    foreach (var removed in state.Hashes.Keys.Except(current)) Remove(removed);
                    state.Terms = session.Terms.ToDictionary(x => x.Text, StringComparer.OrdinalIgnoreCase);
                    foreach (var item in session.Terms) Add("term", item.Text, item);
                } else {
                    var changed = new Dictionary<string, HashSet<string>?>();
                    void Changed(string kind, string[]? ids) {
                        if (!changed.TryGetValue(kind, out var set)) changed[kind] = set = [];
                        if (ids is null) changed[kind] = null; else if (set is not null) set.UnionWith(ids);
                    }
                    foreach (var entry in events) {
                        if (entry.Kind == "source") { Changed("transcript", entry.Ids); Changed("chunk", entry.Ids); }
                        else if (entry.Kind is "translation" or "note-status") Changed("transcript", entry.Ids);
                        else if (entry.Kind is "group" or "settings" or "note") Changed("meta", [id]);
                        else Changed(entry.Kind, entry.Ids);
                    }
                    var sources = new List<SyncRecord>();
                    foreach (var (kind, ids) in changed) {
                        var rows = await ReadSyncRecords(id, kind, ids?.ToArray()); state.RecordQueries++;
                        foreach (var row in rows) AddRecord(row);
                        var keys = rows.Select(x => kind + ":" + x.Id).ToHashSet();
                        foreach (var removed in (ids is null ? state.Hashes.Keys.Where(x => x.StartsWith(kind + ":")) : ids.Select(x => kind + ":" + x)).Except(keys)) Remove(removed);
                        if (kind is "transcript" or "material") sources.AddRange(rows);
                    }
                    // Status/translation-only changes do not re-extract the lecture. Only new confirmed
                    // source text/materials can discover terms; affected labels query exact matching IDs.
                    if (events.Any(x => x.Kind is "source" or "material")) {
                        var transcripts = sources.Select(x => x.Value).OfType<Transcript>().ToList();
                        var materials = sources.Select(x => x.Value).OfType<Material>().ToList();
                        var discovered = TermCandidateExtractor.Find(materials, transcripts);
                        var affected = discovered.Select(x => x.Text).Concat(state.Terms.Values.Where(t =>
                            transcripts.Any(x => x.SourceText.Contains(t.Text, StringComparison.OrdinalIgnoreCase)) ||
                            materials.Any(x => x.Text.Contains(t.Text, StringComparison.OrdinalIgnoreCase))).Select(x => x.Text)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                        foreach (var term in affected) {
                            var candidate = await ReadSyncTerm(id, term); state.RecordQueries++;
                            if (candidate is null) state.Terms.Remove(term); else state.Terms[term] = candidate;
                        }
                        var terms = state.Terms.Values.OrderBy(x => x.Text, StringComparer.OrdinalIgnoreCase).Take(MaxSyncedTerms).ToList();
                        state.Terms = terms.ToDictionary(x => x.Text, StringComparer.OrdinalIgnoreCase);
                        var keys = terms.Select(x => "term:" + x.Text).ToHashSet();
                        foreach (var removed in state.Hashes.Keys.Where(x => x.StartsWith("term:")).Except(keys)) Remove(removed);
                        foreach (var term in terms) Add("term", term.Text, term);
                        // A new matching source can extend an already saved explanation's
                        // provenance without a new provider call. Read only those known IDs.
                        var insightIds = affected.Select(term => state.Terms.GetValueOrDefault(term))
                            .OfType<TermCandidate>().Select(term => TermInsight.IdFor(id, term.Text, term.Context, state.NoteLanguage))
                            .Where(insightId => state.Hashes.ContainsKey("insight:" + insightId)).Distinct().ToArray();
                        if (insightIds.Length > 0) {
                            var insights = await ReadSyncRecords(id, "insight", insightIds); state.RecordQueries++;
                            foreach (var insight in insights) AddRecord(insight);
                        }
                    }
                }
                state.Revision = revision;
            }
            var batch = new List<object>(); var size = 0;
            while (state.Pending.TryPeek(out var next) && batch.Count < MaxRecordsPerPage) {
                var bytes = System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(next.Value));
                if (bytes > MaxPageBytes) throw new InvalidOperationException("One session record exceeds the sync payload limit; keep the current view and inspect this record");
                if (size + bytes > MaxPageBytes && batch.Count > 0) break;
                state.Pending.Dequeue(); size += bytes;
                batch.Add(new { kind = next.Kind, value = next.Value });
                if (next.Kind == "removed") state.Hashes.Remove(next.Key); else state.Hashes[next.Key] = next.Hash;
            }
            return new { cursor = key, reset, hasMore = state.Pending.Count > 0, changes = batch,
                diagnostics = new { state.FullReads, state.RecordQueries, revision = state.Revision } };
        } finally { state.Gate.Release(); }
    }
}
