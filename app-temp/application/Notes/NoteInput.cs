using System.Text.Json;
using System.Text.RegularExpressions;
using Playback.Domain.Notes;
using Playback.Domain;

namespace Playback.Application.Notes;

// What the note model sees: which sources go next, which sections come along as context, and the
// bounded JSON input in which every source ID is a short alias (T1…) that the reply is decoded from.
// The input starts with the whole lecture so far in time order, which only grows at its end, so each call
// repeats the previous call's prefix and providers can serve it from their prompt cache. The notes and
// this call's task follow it and change every call.
public static class NoteInput
{
    const int MaxDeferredContextSources = 8;
    const int MaxDeferredContextChars = 3_000;
    const int MaxSources = 64;
    const int MaxSourceChars = 18_000;
    const int MaxMaterialPassages = 2;
    const int MaxContextSections = 3;
    const int MaxProtectedTopics = 40;
    // Everything after the lecture: notes context, the sources to write and materials.
    const int MaxInputChars = 48_000;
    // About 50k tokens of lecture text. A longer lecture drops its oldest speech in whole steps, so the
    // remaining prefix stays identical (and cached) until the next step.
    const int MaxLectureChars = 200_000;
    const int LectureWindowStep = 200;
    // What Jev sees to decide whether a passage is complete: the speech just before it and the passage.
    const int GateRecentSources = 6;
    // One confirmed ASR passage is a few sentences; anything this long is a broken recognition, refused
    // before any provider sees it.
    const int MaxSingleSourceChars = 8_000;
    // Chinese and Cantonese text stays readable: escaping each character as \uXXXX would cost several tokens.
    static readonly JsonSerializerOptions InputJson = new(JsonSerializerDefaults.Web)
        { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // The session's next sources, minus those the user removed from the notes on purpose.
    public static List<NoteTranscript> Pending(NoteSession session) =>
        Pending(session.Transcripts).Where(x => !(session.CurrentNote?.SuppressedSourceIds.Contains(x.Id) ?? false)).ToList();

    // The next confirmed sources, oldest first and bounded in count and length.
    public static List<NoteTranscript> Pending(IEnumerable<NoteTranscript> transcripts)
    {
        var ordered = transcripts
            .Where(x => !string.IsNullOrWhiteSpace(x.SourceText) && x.NoteStatus != "completed" && x.NoteStatus != "suppressed")
            .OrderBy(x => x.RecordedAt).ThenBy(x => x.StartMs).ThenBy(x => x.Id)
            .ToList();
        var active = ordered.Where(x => x.NoteStatus != "deferred").ToList();
        var selected = new List<NoteTranscript>();
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

    // NoteMaterial passages without a written point yet. NoteMaterial IDs in note metadata do not prove a
    // passage was digested; only a point that cites the unchanged passage does.
    public static List<NoteMaterial> Materials(IEnumerable<NoteMaterial> materials, Note? latest) =>
        MaterialSources.Passages(materials)
            .Where(x => !(latest?.SuppressedSourceIds.Contains(x.Id) ?? false) &&
                !(latest?.SuppressedSourceIds.Contains(MaterialSources.Parent(x.Id)) ?? false))
            .Where(x => !(latest?.Coverage.Any(c => c.SourceId == x.Id && c.Status == "covered" && c.ContentHash == ContentHash.Of(x.Text) &&
                c.PointIds.Any(p => latest.Sections.Any(s => s.Points.Any(point => point.Id == p && point.SourceIds.Contains(x.Id))))) ?? false))
            .Take(MaxMaterialPassages)
            .ToList();

    // Editable sections that share words with the new speech; sections near the end win ties.
    public static List<NoteSection> ContextSections(List<NoteSection> sections, List<NoteTranscript> input)
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
    public static NotePrompt Bounded(NoteSession session, List<NoteTranscript> pending, List<NoteSection> context,
        List<NoteMaterial> materials, string trigger)
    {
        while (true)
        {
            var (prompt, lectureChars) = Serialize(session, pending, context, materials, trigger);
            if (prompt.Text.Length - lectureChars <= MaxInputChars) return prompt;
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
    public static NotePrompt Build(NoteSession session, List<NoteTranscript> pending, List<NoteSection> context,
        List<NoteMaterial> materials, string trigger)
    {
        var (prompt, lectureChars) = Serialize(session, pending, context, materials, trigger);
        return prompt.Text.Length - lectureChars <= MaxInputChars ? prompt : throw TooLarge();
    }

    // Jev decides whether the pending passage is complete; it needs the speech just before it and the passage,
    // not the whole lecture. The "trigger" field is left out of the decision's saved identity.
    public static NotePrompt Gate(NoteSession session, List<NoteTranscript> pending, List<NoteMaterial> materials, string trigger)
    {
        RequireBoundedSources(pending);
        var first = pending.FirstOrDefault();
        var recent = first is null ? [] : Chronological(session.Transcripts)
            .TakeWhile(x => x.Id != first.Id).Where(x => x.NoteStatus == "completed").TakeLast(GateRecentSources).ToList();
        var text = JsonSerializer.Serialize(new
        {
            trigger,
            language = session.NoteLanguage,
            topics = session.Sections.TakeLast(MaxProtectedTopics).Select(x => x.Title[..Math.Min(80, x.Title.Length)]),
            before = recent.Select(x => new { x.SourceId, x.StartMs, text = x.SourceText }),
            pending = pending.Select(x => new { x.Id, x.SourceId, x.StartMs, x.EndMs, text = x.SourceText, x.Uncertain }),
            materials = materials.Select(x => new { x.Id, x.Name, text = x.Text[..Math.Min(2_000, x.Text.Length)] })
        }, InputJson);
        return new NotePrompt(text, []);
    }

    static IEnumerable<NoteTranscript> Chronological(IEnumerable<NoteTranscript> transcripts) =>
        transcripts.Where(x => !string.IsNullOrWhiteSpace(x.SourceText)).OrderBy(x => x.RecordedAt).ThenBy(x => x.StartMs).ThenBy(x => x.Id);

    // The lecture window: everything, unless it is longer than the budget; then the oldest speech goes in
    // whole steps so the start of the window moves rarely.
    static (List<NoteTranscript> All, int Start) LectureWindow(NoteSession session)
    {
        var all = Chronological(session.Transcripts).ToList();
        var start = 0;
        while (all.Skip(start).Sum(x => x.SourceText.Length) > MaxLectureChars && start + LectureWindowStep < all.Count) start += LectureWindowStep;
        return (all, start);
    }

    // Topics that take no new blocks (NoteStructure.IsClosed), judged against the newest speech being written.
    public static HashSet<string> ClosedTopics(NoteSession session, IEnumerable<NoteTranscript> pending)
    {
        var spoken = session.Transcripts.ToDictionary(x => x.Id, x => x.StartMs);
        var now = pending.Select(x => x.StartMs).DefaultIfEmpty(spoken.Values.DefaultIfEmpty(0).Max()).Max();
        return session.Sections.Where(topic =>
        {
            var times = topic.Blocks.SelectMany(x => x.Anchors).SelectMany(x => x.SourceIds).Where(spoken.ContainsKey).Select(x => spoken[x]).ToList();
            return NoteStructure.IsClosed(topic, times.Count == 0 ? null : times.Min(), now);
        }).Select(x => x.Id).ToHashSet();
    }

    // Topics, with their subtopics, spoken within NoteStructure.MaxParentGapMinutes of the newest speech being written.
    public static HashSet<string> RecentTopics(NoteSession session, IEnumerable<NoteTranscript> pending)
    {
        var spoken = session.Transcripts.ToDictionary(x => x.Id, x => x.StartMs);
        var now = pending.Select(x => x.StartMs).DefaultIfEmpty(spoken.Values.DefaultIfEmpty(0).Max()).Max();
        return session.Sections.Where(topic => session.Sections.Where(x => x == topic || NoteStructure.IsWithin(x, topic.Id, session.Sections))
                .SelectMany(x => x.Blocks).SelectMany(x => x.Anchors).SelectMany(x => x.SourceIds).Where(spoken.ContainsKey)
                .Any(id => now - spoken[id] <= NoteStructure.MaxParentGapMinutes * 60_000L))
            .Select(x => x.Id).ToHashSet();
    }

    // Like a table of contents with page numbers: when a topic's speech was given, and what its first block says.
    static string? Span(NoteSection topic, NoteSession session)
    {
        var spoken = topic.Blocks.SelectMany(x => x.Anchors).SelectMany(x => x.SourceIds)
            .Select(id => session.Transcripts.FirstOrDefault(t => t.Id == id)?.StartMs).OfType<long>().ToList();
        return spoken.Count == 0 ? null : Clock(spoken.Min()) + "–" + Clock(spoken.Max());
    }

    static string Summary(NoteSection topic)
    {
        var first = topic.Blocks.OrderBy(x => x.Order).FirstOrDefault()?.Body ?? "";
        var text = Regex.Replace(CitationSyntax.Rewrite(first, _ => ""), @"\[\s*\]|[#*_`>|]|\s+", " ").Trim();
        return text.Length > 60 ? text[..60] + "…" : text;
    }

    static string Clock(long ms) => TimeSpan.FromMilliseconds(Math.Max(0, ms)).ToString(ms >= 3_600_000 ? @"h\:mm\:ss" : @"m\:ss");

    static void RequireBoundedSources(IEnumerable<NoteTranscript> pending)
    {
        if (pending.Any(x => x.SourceText.Length > MaxSingleSourceChars))
            throw new InvalidOperationException("One ASR passage exceeds the bounded input; it was not sent to a provider");
    }

    static InvalidOperationException TooLarge() =>
        new("Section context exceeds the bounded input; select a smaller section for revision");
    // A crowded level is regrouped from titles only. crowdedLevels names the parents whose topics are too many ("" for
    // the top level), and levelsBelow how deep each topic's subtree is, so a new parent keeps every topic within
    // NoteStructure.MaxDepth levels.
    public static NotePrompt Outline(NoteSession session, List<NoteSection> scope)
    {
        var aliases = scope.Select((topic, i) => (Alias: "S" + i.ToString("D3"), topic.Id)).ToDictionary(x => x.Alias, x => x.Id);
        var reverse = aliases.ToDictionary(x => x.Value, x => x.Key);
        var text = JsonSerializer.Serialize(new { trigger = "document-outline", language = session.NoteLanguage, baseVersion = session.NoteVersion,
            pending = Array.Empty<object>(), materials = Array.Empty<object>(), editableSections = Array.Empty<object>(),
            crowdedLevels = NoteStructure.CrowdedParents(session.Sections).Select(x => x is null ? "" : reverse.GetValueOrDefault(x, x)),
            topicColumns = new[] { "id", "title", "parent", "order", "protected", "levelsBelow" },
            topicOutline = scope.Select(x => new object?[] {reverse[x.Id], x.Title[..Math.Min(48, x.Title.Length)], x.ParentId is not null ? reverse.GetValueOrDefault(x.ParentId, x.ParentId) : null, x.Order, x.UserEdited,
                NoteStructure.LevelsBelow(x, session.Sections)}) }, new JsonSerializerOptions {Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping});
        return text.Length <= 48000 ? new(text, aliases) : throw TooLarge();
    }

    // Lecture first and in time order (the cached prefix); the notes and this call's task after it.
    static (NotePrompt Prompt, int LectureChars) Serialize(NoteSession session, List<NoteTranscript> pending, List<NoteSection> context,
        List<NoteMaterial> materials, string trigger)
    {
        // A transcript's alias is its position in the whole lecture, so it stays the same from call to call.
        RequireBoundedSources(pending);
        var (lectureAll, windowStart) = LectureWindow(session);
        var reverse = lectureAll.Select((x, i) => (x.Id, Alias: "T" + (i + 1))).ToDictionary(x => x.Id, x => x.Alias);
        var others = materials.Select(x => x.Id)
            .Concat(context.SelectMany(x => x.Points).SelectMany(x => x.SourceIds))
            .Concat(context.SelectMany(x => CitationSyntax.Bodies(x.Markdown)).Where(x => x.StartsWith("cite_"))
                .SelectMany(x => session.CurrentNote?.Citations.FirstOrDefault(c => c.Id == x)?.SourceIds ?? []))
            .Concat(pending.Select(x => x.Id))
            .Where(x => !reverse.ContainsKey(x)).Distinct().ToList();
        foreach (var id in others) reverse[id] = "T" + (reverse.Count + 1);
        var aliases = reverse.ToDictionary(x => x.Value, x => x.Key);
        var allIds = reverse.Keys.ToList();
        var citations = NoteSections.CloneCitations(session.CurrentNote?.Citations ?? []);
        var contextSourceIds = context.SelectMany(s => s.Points).SelectMany(p => p.SourceIds).ToList();
        var contextCitationIds = context.SelectMany(x => CitationSyntax.Bodies(x.Markdown)).SelectMany(x => x.Split([',', ';'], StringSplitOptions.TrimEntries)).ToHashSet();
        var lecture =lectureAll.Skip(windowStart).Select(x => new object[] { reverse[x.Id], x.SourceId, Clock(x.StartMs), x.SourceText, x.Uncertain }).ToList();
        var lectureChars = JsonSerializer.Serialize(lecture, InputJson).Length;
        var closed = ClosedTopics(session, pending);
        var recent = RecentTopics(session, pending);
        var text = JsonSerializer.Serialize(new
        {
            language = session.NoteLanguage,
            lectureColumns = new[] { "id", "source", "start", "text", "uncertain" },
            lecture,
            omittedEarlierSources = windowStart,
            trigger,
            baseVersion = session.NoteVersion,
            // closed: full or long-running; new speech goes into a new topic or a subtopic, never into it.
            // recent: spoken in the last few minutes, so it can take a new subtopic.
            topicOutline = context.Concat(session.Sections.Where(x => !context.Any(c => c.Id == x.Id))).Take(64).Select(x => new { x.Id, x.ParentId, x.Order, title = x.Title[..Math.Min(120, x.Title.Length)], x.UserEdited,
                blockCount = x.Blocks.Count, closed = closed.Contains(x.Id), recent = recent.Contains(x.Id), depth = NoteStructure.Depth(x, session.Sections),
                spoken = Span(x, session), summary = Summary(x),
                blocks = context.Any(c => c.Id == x.Id) ? x.Blocks.Take(32).Select(b => new { b.Id, b.Order, b.Role, b.UserEdited, b.SupersedesBlockId }).ToArray() : [] }),
            remainingOutlineTopics = Math.Max(0, session.Sections.Count - 64),
            // Write these now, in this order. deferredBefore: already deferred once, so it cannot be deferred again.
            pending = pending.Select(x => new { id = reverse[x.Id], x.SourceId, x.StartMs, x.EndMs, x.RecordedAt, text = x.SourceText, x.Uncertain,
                deferredBefore = x.NoteStatus == "deferred" }),
            remainingPending = session.Transcripts.Count(x => x.NoteStatus != "completed" && !string.IsNullOrWhiteSpace(x.SourceText)) - pending.Count,
            materials = materials.Select(x => new { id = reverse[x.Id], x.Name, text = x.Text }),
            sectionMaterials = MaterialSources.Passages(session.Materials).Where(x => contextSourceIds.Contains(x.Id))
                .Select(x => new { id = reverse[x.Id], x.Name, text = x.Text }),
            // The context sections' sources; their text is in the lecture above.
            sectionSources = session.Transcripts.Where(x => contextSourceIds.Contains(x.Id)).Select(x => new { id = reverse[x.Id] }),
            editableSections = context.Select(x => new
            {
                x.Id,
                x.Version,
                x.Title,
                blocks = x.Blocks.Select(b => new {b.Id, b.Role, b.Order, b.Body, b.UserEdited}),
                points = x.Points.Select(p => new
                {
                    p.Id,
                    text = NoteSections.Compact(session.Id, p.Text, allIds, citations),
                    sourceIds = p.SourceIds.Select(id => reverse[id])
                })
            }),
            // Only the persistent citations the context sections show; every alias is known, so filter by use.
            citationSources = citations.Where(c => contextCitationIds.Contains(c.Id) && c.SourceIds.All(reverse.ContainsKey))
                .Select(c => new { c.Id, sourceIds = c.SourceIds.Select(id => reverse[id]) }),
            protectedTopics = session.Sections.Where(x => x.UserEdited).Take(MaxProtectedTopics).Select(x => new { x.Id, x.Title })
        }, InputJson);
        return (new NotePrompt(text, aliases), lectureChars);
    }
}

// The serialized model input and the aliases its reply cites (T1 → the stored source ID).
public sealed record NotePrompt(string Text, Dictionary<string, string> Aliases)
{
    public string Decode(string markdown) => CitationSyntax.Rewrite(markdown, body =>
    {
        var tokens = body.Split([',', ';'], StringSplitOptions.TrimEntries);
        return tokens.All(Aliases.ContainsKey) ? string.Join(", ", tokens.Select(x => Aliases[x])) : body;
    });
}
