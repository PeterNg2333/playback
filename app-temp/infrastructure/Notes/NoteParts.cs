using System.Security.Cryptography;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Playback.Application.Notes;
using Playback.Domain.Notes;
namespace Playback.Infrastructure.Notes;

// A saved note names its topics and segments by content hash instead of copying them. Each topic (with the citations its
// blocks use) and each segment is a part stored once per session, so a version that changes one topic adds one part and
// shares the rest with earlier versions. Markdown, TranscriptIds and Citations are worked out from the topics when read;
// a note where that would give something else keeps its own copy. Every version is read on its own with its parts, so
// there is no chain of patches to replay. Notes saved before this have SectionParts missing and are read as they are.
public static class NoteParts
{
    public sealed record Stored(BsonDocument Note, List<BsonDocument> Parts);

    static readonly string[] Derived = ["Citations", "Markdown", "TranscriptIds"];

    public static bool HasParts(BsonDocument note) => note.Contains("SectionParts");

    public static IEnumerable<string> PartIds(BsonDocument note) => HasParts(note)
        ? note["SectionParts"].AsBsonArray.Concat(note["SegmentParts"].AsBsonArray).Select(hash => PartId(note["SessionId"].AsString, hash.AsString))
        : [];

    public static string PartId(string sessionId, string hash) => sessionId + ":" + hash;

    public static Stored Store(Note note)
    {
        var parts = new Dictionary<string, BsonDocument>();
        string Add(BsonDocument content)
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(content.ToBson()));
            parts.TryAdd(hash, new BsonDocument { { "_id", PartId(note.SessionId, hash) }, { "SessionId", note.SessionId } }.AddRange(content));
            return hash;
        }
        var stored = note.ToBsonDocument();
        stored.Remove("Sections");
        stored.Remove("Segments");
        stored["SectionParts"] = new BsonArray(note.Sections.Select(section => Add(new BsonDocument
        {
            { "Section", section.ToBsonDocument() },
            { "Citations", new BsonArray(UsedBy(section, note.Citations).Select(x => x.ToBsonDocument())) }
        })));
        stored["SegmentParts"] = new BsonArray(note.Segments.Select(segment => Add(new BsonDocument("Segment", segment.ToBsonDocument()))));
        stored["SectionCount"] = note.Sections.Count;

        // Leave out each derived field the parts give back exactly; keep the rest.
        var original = note.ToBsonDocument();
        BsonDocument Part(string hash) => parts[hash];
        var trial = stored.DeepClone().AsBsonDocument;
        foreach (var field in Derived) trial.Remove(field);
        var derived = Read(trial, Part).ToBsonDocument();
        foreach (var field in Derived)
            if (derived[field].Equals(original[field])) stored.Remove(field);
        // A note the parts cannot give back exactly is stored whole, as before.
        return Read(stored, Part).ToBsonDocument().Equals(original) ? new(stored, parts.Values.ToList()) : new(original, []);
    }

    public static Note Read(BsonDocument note, Func<string, BsonDocument> part)
    {
        var result = BsonSerializer.Deserialize<Note>(note);
        if (!HasParts(note)) return result;
        var sections = note["SectionParts"].AsBsonArray.Select(hash => part(hash.AsString)).ToList();
        result.Sections = sections.Select(x => BsonSerializer.Deserialize<NoteSection>(x["Section"].AsBsonDocument)).ToList();
        result.Segments = note["SegmentParts"].AsBsonArray
            .Select(hash => BsonSerializer.Deserialize<NoteSegment>(part(hash.AsString)["Segment"].AsBsonDocument)).ToList();
        if (!note.Contains("Citations"))
            result.Citations = sections.SelectMany(x => x["Citations"].AsBsonArray)
                .Select(x => BsonSerializer.Deserialize<NoteCitation>(x.AsBsonDocument)).DistinctBy(x => x.Id).ToList();
        if (!note.Contains("Markdown")) result.Markdown = NoteTree.Render(result.Sections);
        // As NoteMutation and the repository's save work them out: every cited source except material passages.
        if (!note.Contains("TranscriptIds"))
            result.TranscriptIds = result.Sections.SelectMany(x => x.Blocks).SelectMany(x => x.SourceIds)
                .Concat(result.Citations.SelectMany(x => x.SourceIds)).Distinct()
                .Where(x => !result.MaterialIds.Contains(MaterialSources.Parent(x))).ToList();
        return result;
    }

    static IEnumerable<NoteCitation> UsedBy(NoteSection section, IEnumerable<NoteCitation> citations)
    {
        var ids = section.Blocks.SelectMany(x => CitationSyntax.Bodies(x.Body)).ToHashSet(StringComparer.Ordinal);
        return citations.Where(x => ids.Contains(x.Id));
    }
}
