using System.Net.Http.Json;
using System.Text.Json;

namespace Playback.Api.Services.Ai.Providers;

// Shared discovery, credentials and bounded transport. Each task owns its own questions
// and admission limit; a slow glossary request cannot monopolize the note gate.
public sealed class JevTransport : IDisposable
{
    readonly HttpClient http;
    readonly SemaphoreSlim discovery = new(1, 1);
    string? model;
    DateTimeOffset expires;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JEV_API_KEY"));
    public JevTransport() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }
    public JevTransport(HttpMessageHandler handler) => http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    public async Task<(string Model, JsonElement Answers, string? Usage)> Evaluate(string state, object questions, CancellationToken ct) {
        if (Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST") == "yes")
            throw new InvalidOperationException("External Jev requests are disabled for offline tests");
        if (state.Length > 48000) throw new InvalidOperationException("Jev input exceeds the size limit");
        var key = Environment.GetEnvironmentVariable("JEV_API_KEY") ?? throw new InvalidOperationException("Jev credential is unavailable");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await discovery.WaitAsync(timeout.Token);
        try {
            if (model is null || expires <= DateTimeOffset.UtcNow) {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.typesafe.ai/v1/models");
                request.Headers.Authorization = new("Bearer", key);
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    throw new InvalidOperationException("Jev rejected JEV_API_KEY at GET /v1/models (HTTP 401); check API access");
                if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Jev discovery HTTP {(int)response.StatusCode}");
                using var doc = JsonDocument.Parse(await Services.ProviderResponseReader.ReadBounded(response, 100000, timeout.Token));
                model = doc.RootElement.GetProperty("models").EnumerateArray().Select(x => x.GetProperty("name").GetString())
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? throw new InvalidOperationException("Jev returned no available model");
                expires = DateTimeOffset.UtcNow.AddHours(1);
            }
        } finally { discovery.Release(); }
        using var ranked = new HttpRequestMessage(HttpMethod.Post, "https://api.typesafe.ai/v1/systemone");
        ranked.Headers.Authorization = new("Bearer", key);
        ranked.Content = JsonContent.Create(new { model, state, questions });
        using var result = await http.SendAsync(ranked, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!result.IsSuccessStatusCode) throw new HttpRequestException($"Jev systemone HTTP {(int)result.StatusCode}");
        using var parsed = JsonDocument.Parse(await Services.ProviderResponseReader.ReadBounded(result, 100000, timeout.Token));
        return (parsed.RootElement.TryGetProperty("model", out var actual) ? actual.GetString() ?? model! : model!,
            parsed.RootElement.GetProperty("answers").Clone(), parsed.RootElement.TryGetProperty("usage", out var usage) ? usage.GetRawText() : null);
    }
    public void Dispose() { http.Dispose(); discovery.Dispose(); }
}
