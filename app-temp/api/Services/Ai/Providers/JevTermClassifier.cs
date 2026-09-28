using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Playback.Api.Services;

namespace Playback.Api.Services.Ai.Providers;

public sealed class JevTermClassifier
{
    const double MinimumExplainProbability = 0.75;
    const double MinimumCategoryConfidence = 0.50;
    public static string HighlightRule => FormattableString.Invariant(
        $"Explain probability >= {MinimumExplainProbability:0%}; rank high or medium; category confidence >= {MinimumCategoryConfidence:0%}.");
    public static bool ShouldHighlight(JevRankResult result) =>
        result.JevProbability >= MinimumExplainProbability && result.JevConfidence >= MinimumCategoryConfidence &&
        (result.JevRank is "high" or "medium");
    readonly HttpClient http;
    readonly SemaphoreSlim rankGate = new(1, 1);
    readonly ConcurrentDictionary<string, (DateTimeOffset Expires, JevRankResult Result)> cache = new();
    string? cachedModel;
    DateTimeOffset modelExpires;

    public JevTermClassifier() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }
    public JevTermClassifier(HttpMessageHandler handler) =>
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JEV_API_KEY"));

    public async Task<object> Rank(string term, CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST") == "yes")
            throw new InvalidOperationException("External term ranking is disabled for offline tests");
        if (term.Length is < 1 or > 100) throw new InvalidOperationException("Invalid term");
        var cacheKey = term.Trim().ToLowerInvariant();
        if (cache.TryGetValue(cacheKey, out var hit) && hit.Expires > DateTimeOffset.UtcNow)
            return hit.Result with { Cached = true, LatencyMs = 0 };
        await rankGate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(cacheKey, out hit) && hit.Expires > DateTimeOffset.UtcNow)
                return hit.Result with { Cached = true, LatencyMs = 0 };
            var result = await RankUncached(term.Trim(), ct);
            cache[cacheKey] = (DateTimeOffset.UtcNow.AddHours(1), result);
            if (cache.Count > 512)
                foreach (var oldest in cache.OrderBy(x => x.Value.Expires).Take(cache.Count - 512))
                    cache.TryRemove(oldest.Key, out _);
            return result;
        }
        finally { rankGate.Release(); }
    }

    async Task<JevRankResult> RankUncached(string term, CancellationToken ct)
    {
        var rule = term.Length >= 8 || term.Any(char.IsUpper);
        var key = Environment.GetEnvironmentVariable("JEV_API_KEY") ?? throw new InvalidOperationException("Jev credential is unavailable");
        var timer = Stopwatch.StartNew();
        var model = await DiscoverModel(key, ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.typesafe.ai/v1/systemone");
        request.Headers.Authorization = new("Bearer", key);
        request.Content = JsonContent.Create(new
        {
            model,
            state = term,
            questions = new
            {
                explain = new
                {
                    type = "noul",
                    instructions = "Would a short explanation of this academic term be useful to a general class audience? " +
                        "Judge the term, not an individual student's understanding."
                },
                category = new
                {
                    type = "choice",
                    instructions = "Rank this candidate term for a lecture glossary.",
                    criteria = new
                    {
                        high = "Specialized academic term",
                        medium = "Domain-specific but familiar term",
                        low = "Everyday word"
                    }
                }
            }
        });
        using var ranked = await http.SendAsync(request, ct);
        if (!ranked.IsSuccessStatusCode) throw new HttpRequestException($"Jev systemone HTTP {(int)ranked.StatusCode}");
        using var result = JsonDocument.Parse(await ProviderResponseReader.ReadBounded(ranked, 100_000, ct));
        var answers = result.RootElement.GetProperty("answers");
        return new JevRankResult(
            term, rule, model, timer.ElapsedMilliseconds,
            answers.GetProperty("explain").GetProperty("noul").GetDouble(),
            answers.GetProperty("category").GetProperty("choice").GetString() ?? "",
            answers.GetProperty("category").GetProperty("confidence").GetDouble(),
            result.RootElement.GetProperty("usage").Clone(), false);
    }

    async Task<string> DiscoverModel(string key, CancellationToken ct)
    {
        if (cachedModel is not null && modelExpires > DateTimeOffset.UtcNow) return cachedModel;
        using var models = new HttpRequestMessage(HttpMethod.Get, "https://api.typesafe.ai/v1/models");
        models.Headers.Authorization = new("Bearer", key);
        using var response = await http.SendAsync(models, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            throw new InvalidOperationException("Jev rejected JEV_API_KEY at GET /v1/models (HTTP 401); check API access and replace the key if needed");
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Jev model discovery HTTP {(int)response.StatusCode}");
        using var discovered = JsonDocument.Parse(await ProviderResponseReader.ReadBounded(response, 100_000, ct));
        var model = discovered.RootElement.GetProperty("models")
            .EnumerateArray()
            .Select(x => x.GetProperty("name").GetString())
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?? throw new InvalidOperationException("Jev returned no available model");
        cachedModel = model;
        modelExpires = DateTimeOffset.UtcNow.AddHours(1);
        return model;
    }
}

public sealed record JevRankResult(
    string Term, bool RuleSuggestExplanation, string Model, long LatencyMs,
    double JevProbability, string JevRank, double JevConfidence, JsonElement Usage, bool Cached);
