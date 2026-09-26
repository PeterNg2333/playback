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
}
