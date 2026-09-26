using Playback.Api.Db;
using Playback.Api.Endpoints;
using System.Text.RegularExpressions;

namespace Playback.Api.Services.Ai.Agents;

public sealed record ChatContext(
    string Prompt,
    Transcript[] RelevantTranscripts,
    Material[] Materials,
    Transcript? FocusedTranscript,
    Material? FocusedMaterial);

public static class ChatContextBuilder
{
    static readonly Regex Word = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled);
    static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "after", "before", "class", "could", "does", "from", "have", "lecture", "please",
        "said", "that", "their", "there", "these", "this", "what", "when", "where", "which", "with", "would",
        "was", "were", "the", "and", "for", "you", "your", "are", "but", "not", "how", "why"
    };
    public static ChatContext Build(SessionView session, QuestionInput input)
    {
        var terms = Word.Matches(input.Question)
            .Select(x => x.Value)
            .Where(x => x.Length > 2 && !StopWords.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(15).ToArray();
        var focused = input.TranscriptId is null
            ? null
            : session.Transcripts.SingleOrDefault(x => x.Id == input.TranscriptId)
                ?? throw new InvalidOperationException("Selected transcript is not in this session");
        if (focused is not null && input.SelectedText is not null &&
            (input.SelectedText.Length > 1000 ||
             !focused.Original.Contains(input.SelectedText, StringComparison.Ordinal)))
            throw new InvalidOperationException("Selected text must come from the selected transcript");

        var relevant = session.Transcripts
            .Where(x => !string.IsNullOrWhiteSpace(x.Original))
            .Select(x => (
                item: x,
                score: terms.Count(term => x.Original.Contains(term, StringComparison.OrdinalIgnoreCase))
                    + (x.Id == focused?.Id ? 100 : 0)))
            .OrderByDescending(x => x.score)
            .ThenByDescending(x => x.item.StartMs)
            .Take(8)
            .Select(x => x.item)
            .ToArray();
        var lecture = string.Join("\n", relevant.Select(x =>
            $"[{x.Id}, {x.StartMs}-{x.EndMs} ms] {x.Original}"));

        var focusedMaterial = input.MaterialId is null
            ? null
            : session.Materials.SingleOrDefault(x => x.Id == input.MaterialId)
                ?? throw new InvalidOperationException("Selected material is not in this session");
        var materials = session.Materials
            .OrderByDescending(x => x.Id == focusedMaterial?.Id)
            .Take(5)
            .ToArray();
        var material = string.Join("\n", materials.Select(x =>
            $"[{x.Id}] {x.Text[..Math.Min(x.Text.Length, 3000)]}"));
        var selected = focused is null
            ? ""
            : $"Selected source [{focused.Id}, {focused.StartMs}-{focused.EndMs} ms]: " +
              $"{input.SelectedText ?? focused.Original}\n";
        var prompt = $"Selected transcript:\n{selected}Lecture:\n{lecture}\nMaterials:\n{material}\nQuestion: {input.Question}";

        return new ChatContext(prompt, relevant, materials, focused, focusedMaterial);
    }
}
