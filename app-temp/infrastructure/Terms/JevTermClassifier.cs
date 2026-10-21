using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Playback.Api.Providers;
using Playback.Domain.Assistance;

namespace Playback.Api.Terms;

public sealed class JevTermClassifier : ITermClassifier
{
    public static string HighlightRule => TermRanking.HighlightRule;
    public static bool ShouldHighlight(JevRankResult result) => TermRanking.ShouldHighlight(result);
    readonly JevTransport transport;
    readonly SemaphoreSlim rankGate = new(1, 1);
    readonly ConcurrentDictionary<string, (DateTimeOffset Expires, JevRankResult Result)> cache = new();
    public JevTermClassifier() : this(new JevTransport()) { }
    public JevTermClassifier(HttpMessageHandler handler) : this(new JevTransport(handler)) { }
    public JevTermClassifier(JevTransport transport) => this.transport = transport;
    public bool IsConfigured => transport.IsConfigured;

    public async Task<JevRankResult> Rank(string term, CancellationToken ct, string context = "")
    {
        if (PlaybackEnvironment.Offline)
            throw new InvalidOperationException("External term ranking is disabled for offline tests");
        if (term.Length is < 1 or > 100) throw new InvalidOperationException("Invalid term");
        if (context.Length > 2000) throw new InvalidOperationException("Term context is too long");
        var cacheKey = term.Trim().ToLowerInvariant() + "\n" + context;
        if (cache.TryGetValue(cacheKey, out var hit) && hit.Expires > DateTimeOffset.UtcNow)
            return hit.Result with { Cached = true, LatencyMs = 0 };
        await rankGate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(cacheKey, out hit) && hit.Expires > DateTimeOffset.UtcNow)
                return hit.Result with { Cached = true, LatencyMs = 0 };
            var result = await RankUncached(term.Trim(), context, ct);
            cache[cacheKey] = (DateTimeOffset.UtcNow.AddHours(1), result);
            if (cache.Count > 512)
                foreach (var oldest in cache.OrderBy(x => x.Value.Expires).Take(cache.Count - 512))
                    cache.TryRemove(oldest.Key, out _);
            return result;
        }
        finally { rankGate.Release(); }
    }

    public static object Questions => TermRanking.Questions;
    public static string State(string term, string context) => TermRanking.State(term, context);
    async Task<JevRankResult> RankUncached(string term, string context, CancellationToken ct)
    {
        var rule = term.Length >= 8 || term.Any(char.IsUpper);
        var timer = Stopwatch.StartNew();
        var response = await transport.Evaluate(State(term, context), Questions, ct);
        var answers = response.Answers;
        using var usage = JsonDocument.Parse(response.Usage ?? "{}");
        return new JevRankResult(term, rule, response.Model, timer.ElapsedMilliseconds,
            answers.GetProperty("explain").GetProperty("noul").GetDouble(),
            answers.GetProperty("category").GetProperty("choice").GetString() ?? "",
            answers.GetProperty("category").GetProperty("confidence").GetDouble(), usage.RootElement.Clone(), false);
    }

}
