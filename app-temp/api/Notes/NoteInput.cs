using System.Text.Json;
using System.Text.RegularExpressions;
using Playback.Api.Db;
using Playback.Api.Sources;

namespace Playback.Api.Notes;

// What the note model sees: which sources go next, which sections come along as context, and the
// bounded JSON input in which every source ID is a short alias (T001…) that the reply is decoded from.
public static class NoteInput
{
    const int MaxDeferredContextSources = 8;
    const int MaxDeferredContextChars = 3_000;
    const int MaxSources = 64;
    const int MaxSourceChars = 18_000;
    const int MaxMaterialPassages = 2;
    const int MaxContextSections = 3;
    const int MaxProtectedTopics = 40;
    const int MaxInputChars = 48_000;

    // The session's next sources, minus those the user removed from the notes on purpose.
    public static List<Transcript> Pending(SessionView session) =>
        Pending(session.Transcripts).Where(x => !(session.CurrentNote?.SuppressedSourceIds.Contains(x.Id) ?? false)).ToList();

    // The next confirmed sources, oldest first and bounded in count and length.
    public static List<Transcript> Pending(IEnumerable<Transcript> transcripts)
    {
        var ordered = transcripts
            .Where(x => !string.IsNullOrWhiteSpace(x.SourceText) && x.NoteStatus != "completed" && x.NoteStatus != "suppressed")
            .OrderBy(x => x.RecordedAt).ThenBy(x => x.StartMs).ThenBy(x => x.Id)
            .ToList();
        var active = ordered.Where(x => x.NoteStatus != "deferred").ToList();
        var selected = new List<Transcript>();
        var chars = 0;
        // A deferred window must not monopolize admission forever. Carry complete nearby fragments
        // as context for new speech; keep every older deferred source readable and unresolved.
        if (active.Count > 0)
        {
            var nearbyDeferred = ordered.TakeWhile(x => x.Id != active[0].Id)
                .Where(x => x.NoteStatus == "deferred" && x.SourceId == active[0].SourceId)
                .Reverse()
                .Take(MaxDeferredContextSources);
            foreach (var source in nearbyDeferred)
            {
                if (chars + source.SourceText.Length > MaxDeferredContextChars) continue;
                selected.Add(source);
                chars += source.SourceText.Length;
            }
        }
        foreach (var source in active.Count > 0 ? active : ordered)
        {
            if (selected.Count >= MaxSources || selected.Count > 0 && chars + source.SourceText.Length > MaxSourceChars) break;
            selected.Add(source);
            chars += source.SourceText.Length;
        }
        if (active.Count > 0 && !selected.Any(x => x.NoteStatus != "deferred")) return [active[0]];
        return selected.OrderBy(x => x.RecordedAt).ThenBy(x => x.StartMs).ThenBy(x => x.Id).ToList();
    }

    // Material passages without a written point yet. Material IDs in note metadata do not prove a
    // passage was digested; only a point that cites the unchanged passage does.
    public static List<Material> Materials(IEnumerable<Material> materials, Note? latest) =>
        MaterialSources.Passages(materials)
            .Where(x => !(latest?.SuppressedSourceIds.Contains(x.Id) ?? false) &&
                !(latest?.SuppressedSourceIds.Contains(MaterialSources.Parent(x.Id)) ?? false))
            .Where(x => !(latest?.Coverage.Any(c => c.SourceId == x.Id && c.Status == "covered" && c.ContentHash == ContentHash.Of(x.Text) &&
                c.PointIds.Any(p => latest.Sections.Any(s => s.Points.Any(point => point.Id == p && point.SourceIds.Contains(x.Id))))) ?? false))
            .Take(MaxMaterialPassages)
            .ToList();

    // Editable sections that share words with the new speech; sections near the end win ties.
    public static List<NoteSection> ContextSections(List<NoteSection> sections, List<Transcript> input)
    {
        var words = Regex.Matches(string.Join(" ", input.Select(x => x.SourceText)), @"[A-Za-z]{4,}|[\p{IsCJKUnifiedIdeographs}]{2,}")
            .Select(x => x.Value.ToLowerInvariant())
            .ToHashSet();
        return sections.Where(x => !x.UserEdited)
            .Select((x, i) => new
            {
                Section = x,
                Score = words.Count(w => x.Markdown.Contains(w, StringComparison.OrdinalIgnoreCase)) * 10 + (i >= sections.Count - 2 ? 1 : 0)
            })
            .OrderByDescending(x => x.Score)
            .Take(MaxContextSections)
            .Where(x => x.Score > 0)
            .Select(x => x.Section)
            .ToList();
    }

    // Fits the input to the model's budget by dropping whole optional parts: context sections, then
    // materials, then deferred and finally the newest sources. Source text and points are never cut.
    public static NotePrompt Bounded(SessionView session, List<Transcript> pending, List<NoteSection> context,
        List<Material> materials, string trigger)
    {
        while (true)
        {
            var prompt = Serialize(session, pending, context, materials, trigger);
            if (prompt.Text.Length <= MaxInputChars) return prompt;
            var canDrop = context.Count > 0 || pending.Count > 1 || materials.Count > 1;
            if (!canDrop || pending.Count == 0 && materials.Count == 0) throw TooLarge();
            if (context.Count > 0) context.RemoveAt(context.Count - 1);
            else if (materials.Count > 1) materials.RemoveAt(materials.Count - 1);
            else if (pending.Any(x => x.NoteStatus == "deferred") && pending.Any(x => x.NoteStatus != "deferred"))
                pending.RemoveAt(pending.FindIndex(x => x.NoteStatus == "deferred"));
            else pending.RemoveAt(pending.Count - 1);
        }
    }

    // The input as given, for a task that must see one whole section.
    public static NotePrompt Build(SessionView session, List<Transcript> pending, List<NoteSection> context,
        List<Material> materials, string trigger)
    {
        var prompt = Serialize(session, pending, context, materials, trigger);
        return prompt.Text.Length <= MaxInputChars ? prompt : throw TooLarge();
    }

    static InvalidOperationException TooLarge() =>
        new("Section context exceeds the bounded input; select a smaller section for revision");

    // The property order is part of the note gate's saved input identity; changing it re-asks Jev once per session.
    static NotePrompt Serialize(SessionView session, List<Transcript> pending, List<NoteSection> context,
        List<Material> materials, string trigger)
    {
        var allIds = pending.Select(x => x.Id)
            .Concat(materials.Select(x => x.Id))
            .Concat(context.SelectMany(x => x.Points).SelectMany(x => x.SourceIds))
            .Concat(context.SelectMany(x => SourceReferences.CitationBodies(x.Markdown)).Where(x => x.StartsWith("cite_"))
                .SelectMany(x => session.CurrentNote?.Citations.FirstOrDefault(c => c.Id == x)?.SourceIds ?? []))
            .Distinct()
            .ToList();
        var aliases = allIds.Select((id, i) => (id, alias: "T" + (i + 1).ToString("D3"))).ToDictionary(x => x.alias, x => x.id);
        var reverse = aliases.ToDictionary(x => x.Value, x => x.Key);
        var citations = NoteSections.CloneCitations(session.CurrentNote?.Citations ?? []);
        var contextSourceIds = context.SelectMany(s => s.Points).SelectMany(p => p.SourceIds).ToList();
        var text = JsonSerializer.Serialize(new
        {
            trigger,
            language = session.NoteLanguage,
            baseVersion = session.NoteVersion,
            pending = pending.Select(x => new { id = reverse[x.Id], x.SourceId, x.StartMs, x.EndMs, x.RecordedAt, text = x.SourceText, x.Uncertain }),
            remainingPending = session.Transcripts.Count(x => x.NoteStatus != "completed" && !string.IsNullOrWhiteSpace(x.SourceText)) - pending.Count,
            materials = materials.Select(x => new { id = reverse[x.Id], x.Name, text = x.Text }),
            sectionMaterials = MaterialSources.Passages(session.Materials).Where(x => contextSourceIds.Contains(x.Id))
                .Select(x => new { id = reverse[x.Id], x.Name, text = x.Text }),
            sectionSources = session.Transcripts.Where(x => contextSourceIds.Contains(x.Id))
                .Select(x => new { id = reverse[x.Id], x.SourceId, x.StartMs, x.EndMs, text = x.SourceText, x.Uncertain }),
            editableSections = context.Select(x => new
            {
                x.Id,
                x.Version,
                x.Title,
                markdown = NoteSections.Compact(session.Id, x.Markdown, allIds, citations),
                points = x.Points.Select(p => new
                {
                    p.Id,
                    text = NoteSections.Compact(session.Id, p.Text, allIds, citations),
                    sourceIds = p.SourceIds.Select(id => reverse[id])
                })
            }),
            citationSources = citations.Where(c => c.SourceIds.All(reverse.ContainsKey))
                .Select(c => new { c.Id, sourceIds = c.SourceIds.Select(id => reverse[id]) }),
            protectedTopics = NoteSections.Current(session).Where(x => x.UserEdited).Take(MaxProtectedTopics).Select(x => new { x.Id, x.Title })
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new NotePrompt(text, aliases);
    }
}

// The serialized model input and the aliases its reply cites (T001 → the stored source ID).
public sealed record NotePrompt(string Text, Dictionary<string, string> Aliases)
{
    public string Decode(string markdown) => Regex.Replace(markdown, @"\[([^\[\]\r\n]+)\]", m =>
    {
        var tokens = m.Groups[1].Value.Split([',', ';'], StringSplitOptions.TrimEntries);
        return tokens.All(Aliases.ContainsKey) ? "[" + string.Join(", ", tokens.Select(x => Aliases[x])) + "]" : m.Value;
    });
}
