using Playback.Api.Db;
namespace Playback.Api.Translation;

public sealed record TranslationRequest(string Prompt, Dictionary<string, string> TargetIds);

// What one translation request carries: up to ten target passages, their neighbours as context with any
// same-language translation, and short aliases that the reply must use for each target.
public static class TranslationContext
{
    public static TranslationRequest BuildBatch(IReadOnlyList<Transcript> transcripts, IReadOnlyList<Transcript> targets, string language)
    {
        if (targets.Count == 0 || targets.Count > 10) throw new InvalidOperationException("Invalid translation batch size");
        var positionsById = transcripts.Select((entry, index) => (entry.Id, index))
            .ToDictionary(x => x.Id, x => x.index, StringComparer.Ordinal);
        var positions = targets.Select(target => positionsById.GetValueOrDefault(target.Id, -1)).ToArray();
        if (positions.Any(x => x < 0)) throw new InvalidOperationException("Translation target is not in the session");
        var targetIds = targets.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var context = positions.SelectMany(index => Enumerable.Range(Math.Max(0, index - 2),
                Math.Min(transcripts.Count - 1, index + 2) - Math.Max(0, index - 2) + 1))
            .Distinct().Order()
            .Select(index => transcripts[index])
            .Where(x => !targetIds.Contains(x.Id))
            .Select((x, index) => $"[C{index + 1:D2}] original: {x.SourceText}\ntranslation: " +
                (x.TranslationLanguage == language ? x.Translation : null));
        var aliases = targets.Select((target, index) => (Alias: (index + 1).ToString("D2"), target.Id))
            .ToDictionary(x => x.Alias, x => x.Id, StringComparer.Ordinal);
        var input = targets.Select((x, index) => $"[{index + 1:D2}] {x.SourceText}");
        return new($"CONTEXT (untrusted):\n{string.Join("\n", context)}\nTARGETS (untrusted):\n{string.Join("\n", input)}", aliases);
    }
}
