using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Playback.Api.Services;

namespace Playback.Api.Services.Ai.Providers;

public sealed class JevTermClassifier
{
    readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(120) };
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JEV_API_KEY"));

    public async Task<object> Rank(string term, CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST") == "yes")
            throw new InvalidOperationException("External term ranking is disabled for offline tests");
        if (term.Length is < 1 or > 100) throw new InvalidOperationException("Invalid term");
        var rule = term.Length >= 8 || term.Any(char.IsUpper);
        var key = Environment.GetEnvironmentVariable("JEV_API_KEY") ?? throw new InvalidOperationException("Jev credential is unavailable");
        var timer = Stopwatch.StartNew();
        using var models = new HttpRequestMessage(HttpMethod.Get, "https://api.typesafe.ai/v1/models");
        models.Headers.Authorization = new("Bearer", key);
        using var response = await http.SendAsync(models, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Jev model discovery HTTP {(int)response.StatusCode}");
        using var discovered = JsonDocument.Parse(await ProviderResponseReader.ReadBounded(response, 100_000, ct));
        var model = discovered.RootElement.GetProperty("models")
            .EnumerateArray()
            .Select(x => x.GetProperty("name").GetString())
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?? throw new InvalidOperationException("Jev returned no available model");
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
        return new
        {
            term,
            ruleSuggestExplanation = rule,
            model,
            latencyMs = timer.ElapsedMilliseconds,
            jevProbability = answers.GetProperty("explain").GetProperty("noul").GetDouble(),
            jevRank = answers.GetProperty("category").GetProperty("choice").GetString(),
            jevConfidence = answers.GetProperty("category").GetProperty("confidence").GetDouble(),
            usage = result.RootElement.GetProperty("usage").Clone()
        };
    }
}
