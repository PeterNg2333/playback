using Playback.Api.Db;
namespace Playback.Api.Services.Ai.Agents;

public static class TranslationContext
{
    public static string Build(IReadOnlyList<Transcript> transcripts, Transcript target, string language)
    {
        var index = transcripts.ToList().FindIndex(x => x.Id == target.Id);
        if (index < 0) throw new InvalidOperationException("Translation target is not in the session");
        var neighbors = transcripts.Skip(Math.Max(0, index - 2)).Take(5)
            .Where(x => x.Id != target.Id)
            .Select(x => $"[{x.Id}] original: {x.Original}\ntranslation: " +
                (x.TranslationLanguage == language ? x.Translation : null));
        return $"CONTEXT (untrusted):\n{string.Join("\n", neighbors)}\nTARGET [{target.Id}] (untrusted): {target.Original}";
    }

    public static string BuildBatch(IReadOnlyList<Transcript> transcripts, IReadOnlyList<Transcript> targets, string language)
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
            .Select(x => $"[{x.Id}] original: {x.Original}\ntranslation: " +
                (x.TranslationLanguage == language ? x.Translation : null));
        var input = targets.Select(x => $"[{x.Id}] {x.Original}");
        return $"CONTEXT (untrusted):\n{string.Join("\n", context)}\nTARGETS (untrusted):\n{string.Join("\n", input)}";
    }
}
