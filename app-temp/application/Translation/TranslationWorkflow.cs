using Playback.Api.Db; using Playback.Api.Activity; using Playback.Application.Translation; using System.Text; using System.Text.Json;
namespace Playback.Api.Translation;
public sealed class TranslationWorkflow(ITranslationRepository store, ITextGenerator gemini, IWorkLog logger, AiActivity activity)
{
    public static string Instructions(string language) => TranslationContext.Instructions(language);
    public async Task Scan(CancellationToken ct, Func<string, bool>? allowed = null)
    {
        foreach (var session in (await store.PendingTranslations()).GroupBy(x => x.SessionId))
        {
            if (allowed is not null && !allowed(session.Key)) continue;
            foreach (var batch in session.Chunk(10)) await TranslateBatch(batch, ct);
        }
    }
    public async Task TranslateBatch(Transcript[] targets, CancellationToken ct)
    {
        if (targets.Length == 0) return;
        if (targets.Length > 10 || targets.Select(x => x.SessionId).Distinct().Count() != 1) throw new InvalidOperationException("Translation batch must contain at most ten targets from one session");
        var session = await store.Session(targets.First().SessionId);
        if (session is null || !session.TranslationEnabled) return;
        var call = await activity.Begin(session.Id, "Translation", "vertex", gemini.Model, targets.Select(x => x.Id));
        await activity.Start(call);
        try
        {
            var request = TranslationContext.BuildBatch(session.Transcripts, targets, session.TranslationLanguage);
            activity.Context(call, "translation-v1", Instructions(session.TranslationLanguage), request.Prompt);
            logger.LogInformation("Translation request input: {TranscriptCount} transcripts, {InputBytes} UTF-8 bytes",
                targets.Length, Encoding.UTF8.GetByteCount(request.Prompt));
            var providerWatch = System.Diagnostics.Stopwatch.StartNew();
            string translated;
            try { translated = await gemini.Generate("PlaybackTranslator",
                Instructions(session.TranslationLanguage),
                request.Prompt,
                ct, onUsage: usage => call.UsageJson = usage); }
            finally { call.ProviderLatencyMs = providerWatch.ElapsedMilliseconds; }
            var values = ParseBatch(translated, request.TargetIds.Keys.ToArray())
                .ToDictionary(x => request.TargetIds[x.Key], x => x.Value, StringComparer.Ordinal);
            foreach (var target in targets)
                await store.SetTranslationResult(target, session.TranslationLanguage, values[target.Id], null);
            await activity.End(call, "completed", $"{targets.Length} translations saved");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            await activity.Fail(call, ex);
            foreach (var target in targets)
                await store.SetTranslationResult(target, session.TranslationLanguage, null, ex.Message);
            logger.LogWarning(ex, "Translation failed for {TranscriptCount} transcripts", targets.Length);
        }
    }

    public static Dictionary<string, string> ParseBatch(string response, IReadOnlyCollection<string> targetIds)
    {
        var json = response.Trim();
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = json.IndexOf('\n');
            var closing = json.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline < 0 || closing <= firstNewline) throw new InvalidOperationException("Invalid translation JSON");
            json = json[(firstNewline + 1)..closing].Trim();
        }
        using var document = JsonDocument.Parse(json);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in document.RootElement.GetProperty("translations").EnumerateArray())
        {
            var id = entry.GetProperty("id").GetString() ?? "";
            var text = entry.GetProperty("text").GetString()?.Trim() ?? "";
            if (!targetIds.Contains(id) || text.Length == 0 || !result.TryAdd(id, text))
                throw new InvalidOperationException("Translation response has missing, duplicate, or unknown targets");
        }
        if (result.Count != targetIds.Count) throw new InvalidOperationException("Translation response omitted a target");
        return result;
    }

}
