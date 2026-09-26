using System.Net.Http.Json;
using System.Text.Json;
using Google.GenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Playback.Api.Services;

namespace Playback.Api.Services.Ai.Providers;

public sealed class GeminiLanguageModel
{
    readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(120) };
    public bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GOOGLE_AI_STUDIO_API_KEY"));

    public async Task<string> Generate(string name, string instructions, string prompt, CancellationToken ct, string model = "gemini-3.8-flash")
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST") == "yes")
            throw new InvalidOperationException("External AI is disabled for offline tests");
        var key = Environment.GetEnvironmentVariable("GOOGLE_AI_STUDIO_API_KEY")
            ?? throw new InvalidOperationException("Google AI Studio key is unavailable");
        var agent = new ChatClientAgent(new Client(vertexAI: false, apiKey: key).AsIChatClient(model), name: name, instructions: instructions);
        var response = await agent.RunAsync(prompt, cancellationToken: ct);
        return response.ToString();
    }

    public Task<GroundedResult> GroundedExplain(string term, CancellationToken ct) =>
        Grounded(term, false, ct);
    public Task<GroundedResult> GroundedSearch(string question, CancellationToken ct) =>
        Grounded(question, true, ct);
    async Task<GroundedResult> Grounded(string value, bool question, CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST") == "yes")
            throw new InvalidOperationException("External search is disabled for offline tests");
        if (value.Length is < 1 or > 1000) throw new InvalidOperationException("Invalid grounded request");
        var key = Environment.GetEnvironmentVariable("GOOGLE_AI_STUDIO_API_KEY")
            ?? throw new InvalidOperationException("Google AI Studio key is unavailable");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://generativelanguage.googleapis.com/v1beta/interactions");
        request.Headers.Add("x-goog-api-key", key);
        request.Content = JsonContent.Create(new
        {
            model = "gemini-3.5-flash-lite",
            input = question
                ? $"Answer this question using public web evidence. Label uncertainty: {value}"
                : $"Explain this academic term concisely and say when evidence is uncertain: {value}",
            tools = new[] { new { type = "google_search" } }
        });
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Gemini grounding HTTP {(int)response.StatusCode}");
        var bytes = await ProviderResponseReader.ReadBounded(response, 1_000_000, ct);
        return ParseGrounding(bytes);
    }
    public static GroundedResult ParseGrounding(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        var evidence = new List<WebEvidence>();
        var answer = "";
        if (doc.RootElement.TryGetProperty("steps", out var steps))
        {
            foreach (var step in steps.EnumerateArray())
            {
                if (!step.TryGetProperty("type", out var kind) || kind.GetString() != "model_output" ||
                    !step.TryGetProperty("content", out var contents)) continue;
                foreach (var content in contents.EnumerateArray())
                {
                    if (content.TryGetProperty("text", out var text)) answer += text.GetString();
                    if (!content.TryGetProperty("annotations", out var annotations)) continue;
                    foreach (var annotation in annotations.EnumerateArray())
                    {
                        if (!annotation.TryGetProperty("url", out var url) ||
                            !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) ||
                            uri.Scheme != "https") continue;
                        evidence.Add(new WebEvidence(
                            "web",
                            uri.ToString(),
                            annotation.TryGetProperty("title", out var title) ? title.GetString() ?? uri.Host : uri.Host,
                            annotation.TryGetProperty("start_index", out var start) ? start.GetInt32() : 0,
                            annotation.TryGetProperty("end_index", out var end) ? end.GetInt32() : 0));
                    }
                }
            }
        }
        if (string.IsNullOrWhiteSpace(answer)) throw new InvalidOperationException("Gemini grounding returned no answer");
        return new GroundedResult(answer, evidence, true);
    }

}

public sealed record WebEvidence(string Kind, string Url, string Title, int StartIndex, int EndIndex);
public sealed record GroundedResult(string Answer, List<WebEvidence> Evidence, bool Inference);
