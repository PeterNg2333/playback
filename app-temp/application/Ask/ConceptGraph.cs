using Playback.Api.Activity;
using Playback.Api.Db;
using Playback.Api.Sources;

namespace Playback.Api.Ask;

// The LightRAG concept graph: LLM-extracted entities and relations over the lecture, kept in a localhost sidecar with
// one workspace per session. It chooses which passages Ask reads and lists concepts for trace; it is never used to decide
// which notes a transcript change affects (that is Lineage, which is exact).
public sealed record ConceptEntity(string Name, string Type, string Description, List<string> DocumentIds);
public sealed record ConceptRelation(string Source, string Target, string Description, string Keywords, List<string> DocumentIds);
public sealed record ConceptChunk(string DocumentId, string Content);
public sealed record ConceptResult(List<ConceptEntity> Entities, List<ConceptRelation> Relations, List<ConceptChunk> Chunks)
{
    public static readonly ConceptResult Empty = new([], [], []);
}
public sealed record ConceptUpsert(int Inserted, int Replaced, int Unchanged, List<string> Failed);

public interface IConceptGraph
{
    bool Enabled { get; }
    Task<ConceptUpsert> Upsert(string sessionId, string language, IReadOnlyList<ConceptDocument> documents, CancellationToken ct);
    Task Delete(string sessionId, IReadOnlyList<string> documentIds, CancellationToken ct);
    Task<ConceptResult> Query(string sessionId, string question, CancellationToken ct);
    Task<ConceptResult> Graph(string sessionId, int limit, CancellationToken ct);
}

// Keeps a session's graph in step with its active transcript. Unchanged documents are skipped by hash, here and in the
// sidecar, so a restart only re-sends hashes.
public sealed class ConceptIndex(IConceptGraph graph, AiActivity activity)
{
    public const int MaxDocumentsPerCall = 16;
    readonly Dictionary<string, Dictionary<string, string>> sent = new(StringComparer.Ordinal);

    public async Task Sync(SessionView session, bool recording, CancellationToken ct)
    {
        if (!graph.Enabled) return;
        var documents = ConceptDocuments.Build(session.Id, session.Transcripts, recording);
        Dictionary<string, string> known;
        lock (sent) known = sent.TryGetValue(session.Id, out var value) ? new(value) : [];
        var changed = documents.Where(x => known.GetValueOrDefault(x.Id) != x.Hash).ToList();
        var removed = known.Keys.Where(id => documents.All(x => x.Id != id)).ToList();
        if (changed.Count == 0 && removed.Count == 0) return;
        var call = await activity.Begin(session.Id, "Concept graph index", "lightrag", "sidecar", changed.SelectMany(x => x.TranscriptIds));
        activity.Context(call, "concept-v1", "Index one-minute lecture documents into the session's LightRAG workspace; replace changed ones.",
            string.Join("|", changed.Select(x => x.Id)));
        try
        {
            await activity.Start(call);
            int inserted = 0, replaced = 0, unchanged = 0; var failed = new List<string>();
            foreach (var batch in changed.Chunk(MaxDocumentsPerCall))
            {
                var result = await graph.Upsert(session.Id, session.NoteLanguage, batch, ct);
                inserted += result.Inserted; replaced += result.Replaced; unchanged += result.Unchanged; failed.AddRange(result.Failed);
                foreach (var document in batch.Where(x => !result.Failed.Contains(x.Id))) known[document.Id] = document.Hash;
            }
            if (removed.Count > 0) { await graph.Delete(session.Id, removed, ct); foreach (var id in removed) known.Remove(id); }
            lock (sent) sent[session.Id] = known;
            await activity.End(call, failed.Count == changed.Count && changed.Count > 0 ? "failed" : "completed",
                $"{inserted} documents added, {replaced} replaced, {unchanged} unchanged, {removed.Count} removed, {failed.Count} failed");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested) { await activity.Fail(call, ex); throw; }
    }

    // Transcript IDs of the passages the graph considers relevant, best first; empty when the graph is off or fails.
    public static List<string> Passages(SessionView session, ConceptResult result)
    {
        var documents = ConceptDocuments.Build(session.Id, session.Transcripts, recording: false).ToDictionary(x => x.Id);
        return result.Chunks.Select(x => x.DocumentId)
            .Concat(result.Entities.SelectMany(x => x.DocumentIds))
            .Concat(result.Relations.SelectMany(x => x.DocumentIds))
            .Where(documents.ContainsKey).Distinct(StringComparer.Ordinal)
            .SelectMany(id => documents[id].TranscriptIds).Distinct(StringComparer.Ordinal).ToList();
    }
}
