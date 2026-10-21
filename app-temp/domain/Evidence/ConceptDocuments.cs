using Playback.Api.Db;

namespace Playback.Api.Sources;

// What the concept graph indexes: one document per source per minute of lecture, made of the active transcript rows
// that start in it. A rerun changes a bucket's text and hash, so the graph replaces exactly that document and rebuilds
// the entities it supported. A bucket is closed once later speech exists in its source or the session stopped.
public sealed record ConceptDocument(string Id, string SourceId, long StartMs, long EndMs, string Text, string Hash, List<string> TranscriptIds);

public static class ConceptDocuments
{
    public const long BucketMs = 60_000;
    public const int MaxChars = 20_000;

    public static string IdFor(string sessionId, string sourceId, long bucket) => $"{sessionId}-concept-{sourceId}-{bucket}";

    public static List<ConceptDocument> Build(string sessionId, IEnumerable<Transcript> transcripts, bool recording)
    {
        var rows = transcripts.Where(x => x.Active && !string.IsNullOrWhiteSpace(x.SourceText)).ToList();
        var result = new List<ConceptDocument>();
        foreach (var source in rows.GroupBy(x => x.SourceId))
        {
            var latest = source.Max(x => x.StartMs);
            foreach (var bucket in source.GroupBy(x => x.StartMs / BucketMs).OrderBy(x => x.Key))
            {
                if (recording && latest < (bucket.Key + 1) * BucketMs) continue;
                var ordered = bucket.OrderBy(x => x.StartMs).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
                var text = string.Join("\n", ordered.Select(x => x.SourceText.Trim()));
                if (text.Length > MaxChars) text = text[..MaxChars];
                result.Add(new(IdFor(sessionId, source.Key, bucket.Key), source.Key, ordered[0].StartMs, ordered.Max(x => x.EndMs), text,
                    ContentHash.Of("concept-v1\n" + text), ordered.Select(x => x.Id).ToList()));
            }
        }
        return result;
    }
}
