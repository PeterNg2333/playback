using Playback.Api.Db;
using System.Text.RegularExpressions;
using Playback.Api.Sources;

namespace Playback.Api.Ask;

public sealed record ChatContext(
    string Prompt,
    Transcript[] RelevantTranscripts,
    Material[] Materials,
    Transcript? FocusedTranscript,
    Material? FocusedMaterial,
    SourceReferences? References = null);

public static class ChatContextBuilder
{
    static readonly Regex Word = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled);
    static readonly Regex HanRun = new(@"[\u3400-\u9FFF]+", RegexOptions.Compiled);
    static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "after", "before", "class", "could", "does", "from", "have", "lecture", "please",
        "said", "that", "their", "there", "these", "this", "what", "when", "where", "which", "with", "would",
        "was", "were", "the", "and", "for", "you", "your", "are", "but", "not", "how", "why"
    };
    public static ChatContext Build(SessionView session, QuestionInput input, string retrievalContext = "")
    {
        var searchText = input.Question + " " + retrievalContext;
        var words = Word.Matches(HanRun.Replace(searchText, " "))
            .Select(x => x.Value)
            .Where(x => x.Length > 2 && !StopWords.Contains(x));
        var hanPairs = HanRun.Matches(searchText)
            .SelectMany(run => Enumerable.Range(0, Math.Max(0, run.Length - 1))
                .Select(index => run.Value.Substring(index, 2)));
        var terms = words.Concat(hanPairs)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(15).ToArray();
        var focused = input.TranscriptId is null
            ? null
            : session.Transcripts.SingleOrDefault(x => x.Id == input.TranscriptId)
                ?? throw new InvalidOperationException("Selected transcript is not in this session");
        if (focused is not null && input.SelectedText is not null &&
            (input.SelectedText.Length > 1000 ||
             (!focused.SourceText.Contains(input.SelectedText, StringComparison.Ordinal) &&
              !focused.Original.Contains(input.SelectedText, StringComparison.Ordinal))))
            throw new InvalidOperationException("Selected text must come from the selected transcript");

        var references = new SourceReferences(session);
        var groups = references.Groups
            .Select(x => (
                item: x,
                score: terms.Count(term => references.GroupText(x).Contains(term, StringComparison.OrdinalIgnoreCase))
                    + (focused is not null && x.TranscriptIds.Contains(focused.Id) ? 100 : 0)))
            .OrderByDescending(x => x.score)
            .ThenByDescending(x => x.item.StartMs)
            .Take(5)
            .Select(x => x.item)
            .ToArray();
        var selectedIds = groups.SelectMany(group => group.TranscriptIds).ToHashSet(StringComparer.Ordinal);
        var relevant = session.Transcripts.Where(x => selectedIds.Contains(x.Id))
            .OrderByDescending(x => terms.Count(term => x.SourceText.Contains(term, StringComparison.OrdinalIgnoreCase)) + (x.Id == focused?.Id ? 100 : 0))
            .ThenByDescending(x => x.StartMs).ToArray();
        var lecture = string.Join("\n", groups.OrderBy(x => x.Id, StringComparer.Ordinal).Select(group =>
            $"Source [{group.Id}] ({group.StartMs}-{group.EndMs} ms): {SourceExcerpt(references.GroupText(group), terms, 1600)}"));

        var focusedMaterial = input.MaterialId is null
            ? null
            : session.Materials.SingleOrDefault(x => x.Id == input.MaterialId)
                ?? throw new InvalidOperationException("Selected material is not in this session");
        var materials = session.Materials
            .OrderByDescending(x => (x.Id == focusedMaterial?.Id ? 100 : 0) +
                terms.Count(term => x.Text.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Take(5)
            .ToArray();
        var material = string.Join("\n", materials.OrderBy(x => x.Id, StringComparer.Ordinal).Select(x =>
            $"[{x.Id}] {SourceExcerpt(x.Text, terms, 3000)}"));
        var selected = focused is null
            ? ""
            : $"Selected source [{focused.Id}] ({focused.StartMs}-{focused.EndMs} ms): " +
              $"{input.SelectedText ?? SourceExcerpt(focused.SourceText, terms, 1600)}\n";
        var prompt = references.Encode($"Materials:\n{material}\nLecture:\n{lecture}\nSelected transcript:\n{selected}\nQuestion: {input.Question}");

        return new ChatContext(prompt, relevant, materials, focused, focusedMaterial, references);
    }

    static string SourceExcerpt(string text, string[] terms, int limit)
    {
        if (text.Length <= limit) return text;
        var matchAt = terms.Select(term => text.IndexOf(term, StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0).DefaultIfEmpty(0).Min();
        var start = Math.Min(Math.Max(0, matchAt - 300), text.Length - limit);
        return (start > 0 ? "…" : "") + text.Substring(start, limit) +
            (start + limit < text.Length ? "…" : "");
    }
}
