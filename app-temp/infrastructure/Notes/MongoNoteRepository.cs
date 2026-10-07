using System.Collections.Concurrent;
using MongoDB.Driver;
using Playback.Application.Notes;
using Playback.Domain.Notes;

namespace Playback.Infrastructure.Notes;

// Existing collections remain intact. The shared lock coordinates API language edits;
// cross-process concurrency is a later migration boundary.
public sealed class MongoNoteRepository(IMongoCollection<Note> notes, IMongoCollection<NoteGateRecord> gates,
    INoteEvidence evidence, INoteSettings settings, ConcurrentDictionary<string, SemaphoreSlim> writeLocks,
    Action<string> changed) : INoteRepository
{
    public async Task<List<Note>> History(string id)
    {
        var result = await notes.Find(x => x.SessionId == id).SortByDescending(x => x.Version).Limit(100).ToListAsync();
        result.Reverse();
        return result;
    }
    public Task<Note?> Version(string id, int version) => notes.Find(x => x.SessionId == id && x.Version == version).FirstOrDefaultAsync()!;
    public async Task<object> Page(string id, int? before, int limit)
    {
        if (limit is < 1 or > 50 || before is < 1) throw new InvalidOperationException("Invalid note history page");
        var result = await notes.Find(x => x.SessionId == id && (before == null || x.Version < before))
            .SortByDescending(x => x.Version).Limit(limit + 1).ToListAsync();
        return new { items = result.Take(limit).Select(x => new { x.Version, x.BasedOnVersion, x.Author, x.CreatedAt,
            sectionCount = x.Sections.Count, x.InputHash }), nextBefore = result.Count > limit ? (int?)result[limit - 1].Version : null };
    }
    public Task<NoteGateRecord?> Gate(string id) => gates.Find(x => x.SessionId == id).FirstOrDefaultAsync()!;
    public Task SaveGate(NoteGateRecord item)
    {
        item.Id = item.SessionId;
        // A decision save must not clear a Stop received during provider work.
        return gates.UpdateOneAsync(x => x.Id == item.Id,
            Builders<NoteGateRecord>.Update.Set(x => x.SessionId, item.SessionId).Set(x => x.InputHash, item.InputHash)
                .Set(x => x.Status, item.Status).Set(x => x.Decision, item.Decision).Set(x => x.Probability, item.Probability)
                .Set(x => x.Confidence, item.Confidence).Set(x => x.Model, item.Model).Set(x => x.UsageJson, item.UsageJson)
                .Set(x => x.PromptVersion, item.PromptVersion).Set(x => x.WaitCount, item.WaitCount)
                .Set(x => x.Attempts, item.Attempts).Set(x => x.RetryAt, item.RetryAt).Set(x => x.ChangedAt, item.ChangedAt)
                .Set(x => x.GenerationRequested, item.GenerationRequested).Set(x => x.EvaluatedFlushVersion, item.EvaluatedFlushVersion)
                .Set(x => x.PolicyRevision, item.PolicyRevision).Set(x => x.FirstPendingAt, item.FirstPendingAt)
                .Set(x => x.PolicySnapshot, item.PolicySnapshot)
                .Set(x => x.LastCheckedAt, item.LastCheckedAt).Set(x => x.AdmissionReason, item.AdmissionReason)
                .Set(x => x.EvidenceHash, item.EvidenceHash).Set(x => x.SettledEvidenceHash, item.SettledEvidenceHash)
                .Set(x => x.SettledNoteVersion, item.SettledNoteVersion).Set(x => x.SettledLanguage, item.SettledLanguage)
                .Set(x => x.GateFailure, item.GateFailure)
                .Set(x => x.EvaluatedBoundaryId, item.EvaluatedBoundaryId),
            new UpdateOptions { IsUpsert = true });
    }
    public Task RequestFlush(string id) => gates.UpdateOneAsync(x => x.Id == id,
        Builders<NoteGateRecord>.Update.Set(x => x.SessionId, id).Set(x => x.FlushRequested, true).Inc(x => x.FlushVersion, 1), new UpdateOptions { IsUpsert = true });
    public Task AcknowledgeFlush(string id, int version) => gates.UpdateOneAsync(x => x.Id == id && x.FlushVersion == version,
        Builders<NoteGateRecord>.Update.Set(x => x.FlushRequested, false));

    public async Task<object> Save(string id, SectionUpdate update, string inputHash, int basedOnVersion, string language,
        string author = "agent", List<string>? deleted = null, List<string>? suppressed = null)
    {
        update = NoteMutation.Prepare(id, update);
        var writeLock = writeLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await writeLock.WaitAsync();
        try
        {
            var currentLanguage = await settings.Language(id);
            var previous = await notes.Find(x => x.SessionId == id).SortByDescending(x => x.Version).FirstOrDefaultAsync();
            if (previous?.InputHash == inputHash) return new { previous.Version, previous.Markdown, previous.Author };
            var note = NoteMutation.Create(id, previous, update, inputHash, basedOnVersion, language, currentLanguage, author, deleted, suppressed);
            var provenance = await evidence.Provenance(id, note.TranscriptIds);
            var materialIds = provenance.MaterialIds;
            note.MaterialIds = note.TranscriptIds.Select(MaterialSources.Parent).Intersect(materialIds).Distinct().ToList();
            note.TranscriptIds = note.TranscriptIds.Where(x => !materialIds.Contains(MaterialSources.Parent(x))).ToList();
            note.InputMaterialIds = update.Coverage.Select(x => MaterialSources.Parent(x.SourceId)).Intersect(materialIds).Distinct().ToList();
            note.InputTranscriptIds = note.InputTranscriptIds.Where(x => !materialIds.Contains(MaterialSources.Parent(x))).ToList();
            var linked = provenance.Transcripts;
            if (linked.Count > 0)
            {
                note.ProcessedThroughMs = linked.Max(x => x.EndMs);
                note.SourceFrom = linked.Min(x => x.RecordedAt);
                note.SourceThrough = linked.Max(x => x.RecordedAt?.AddMilliseconds(x.EndMs - x.StartMs));
            }
            await notes.InsertOneAsync(note);
            changed(id);
            return new { note.Version, note.Markdown, note.Author };
        }
        finally { writeLock.Release(); }
    }
}
