using System.Net.Http.Json;
using System.Diagnostics;
using System.Text.Json;
using Google.GenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

public sealed class Providers
{
    readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(120) };
    public bool HasGemini => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GOOGLE_AI_STUDIO_API_KEY"));
    public bool HasJev => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JEV_API_KEY"));

    public async Task<string> Agent(string name, string instructions, string prompt, CancellationToken ct, string model = "gemini-3.8-flash")
    {
        var key = Environment.GetEnvironmentVariable("GOOGLE_AI_STUDIO_API_KEY") ?? throw new InvalidOperationException("Google AI Studio key is unavailable");
        var agent = new ChatClientAgent(new Client(vertexAI: false, apiKey: key).AsIChatClient(model), name: name, instructions: instructions);
        var response = await agent.RunAsync(prompt, cancellationToken: ct);
        return response.ToString();
    }

    public async Task<string> Transcribe(string path, CancellationToken ct)
    {
        var endpoint = Environment.GetEnvironmentVariable("PLAYBACK_ASR_ENDPOINT") ?? "https://dev-aks.setsailapi.com/stt/infer/upload";
        if (endpoint != "https://dev-aks.setsailapi.com/stt/infer/upload") throw new InvalidOperationException("ASR endpoint is not allowlisted");
        if (new FileInfo(path).Length is < 44 or > 25_000_000) throw new InvalidOperationException("ASR WAV must be 44 bytes to 25 MB");
        using var body = new MultipartFormDataContent();
        await using var file = File.OpenRead(path);
        using var content = new StreamContent(file);
        content.Headers.ContentType = new("audio/wav");
        body.Add(content, "file", "audio.wav");
        using var response = await http.PostAsync(endpoint, body, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"SenseVoice HTTP {(int)response.StatusCode}");
        var bytes = await ReadBounded(response, 512_000, ct);
        return ParseAsr(bytes);
    }
    public static string ParseAsr(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        if (root.TryGetProperty("raw", out var raw) && raw.ValueKind == JsonValueKind.String) return raw.GetString()!;
        if (root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String) return text.GetString()!;
        throw new InvalidOperationException("SenseVoice response schema is unrecognized");
    }

    public async Task<GroundedResult> GroundedExplain(string term, CancellationToken ct) => await Grounded(term, false, ct);
    public async Task<GroundedResult> GroundedSearch(string question, CancellationToken ct) => await Grounded(question, true, ct);
    async Task<GroundedResult> Grounded(string value, bool question, CancellationToken ct)
    {
        if (value.Length is < 1 or > 1000) throw new InvalidOperationException("Invalid grounded request");
        var key = Environment.GetEnvironmentVariable("GOOGLE_AI_STUDIO_API_KEY") ?? throw new InvalidOperationException("Google AI Studio key is unavailable");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://generativelanguage.googleapis.com/v1beta/interactions");
        request.Headers.Add("x-goog-api-key", key);
        request.Content = JsonContent.Create(new { model = "gemini-3.5-flash-lite", input = question ? $"Answer this question using public web evidence. Label uncertainty: {value}" : $"Explain this academic term concisely and say when evidence is uncertain: {value}", tools = new[] { new { type = "google_search" } } });
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Gemini grounding HTTP {(int)response.StatusCode}");
        var bytes = await ReadBounded(response, 1_000_000, ct);
        return ParseGrounding(bytes);
    }
    public static GroundedResult ParseGrounding(byte[] bytes)
    {
        using var doc = JsonDocument.Parse(bytes);
        var evidence = new List<WebEvidence>(); var answer = "";
        if (doc.RootElement.TryGetProperty("steps", out var steps)) foreach (var step in steps.EnumerateArray())
        {
            if (!step.TryGetProperty("type", out var kind) || kind.GetString() != "model_output" || !step.TryGetProperty("content", out var contents)) continue;
            foreach (var content in contents.EnumerateArray())
            {
                if (content.TryGetProperty("text", out var text)) answer += text.GetString();
                if (!content.TryGetProperty("annotations", out var annotations)) continue;
                foreach (var annotation in annotations.EnumerateArray())
                {
                    if (!annotation.TryGetProperty("url", out var url) || !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri) || uri.Scheme != "https") continue;
                    evidence.Add(new WebEvidence("web", uri.ToString(), annotation.TryGetProperty("title", out var title) ? title.GetString() ?? uri.Host : uri.Host, annotation.TryGetProperty("start_index", out var start) ? start.GetInt32() : 0, annotation.TryGetProperty("end_index", out var end) ? end.GetInt32() : 0));
                }
            }
        }
        if (string.IsNullOrWhiteSpace(answer)) throw new InvalidOperationException("Gemini grounding returned no answer");
        return new GroundedResult(answer, evidence, true);
    }

    public async Task<object> RankTerm(string term, CancellationToken ct)
    {
        if (term.Length is < 1 or > 100) throw new InvalidOperationException("Invalid term");
        var rule = term.Length >= 8 || term.Any(char.IsUpper);
        var key = Environment.GetEnvironmentVariable("JEV_API_KEY") ?? throw new InvalidOperationException("Jev credential is unavailable");
        var timer = Stopwatch.StartNew();
        using var models = new HttpRequestMessage(HttpMethod.Get, "https://api.typesafe.ai/v1/models");
        models.Headers.Authorization = new("Bearer", key);
        using var response = await http.SendAsync(models, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Jev model discovery HTTP {(int)response.StatusCode}");
        using var discovered = JsonDocument.Parse(await ReadBounded(response, 100_000, ct));
        var model = discovered.RootElement.GetProperty("models").EnumerateArray().Select(x => x.GetProperty("name").GetString()).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? throw new InvalidOperationException("Jev returned no available model");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.typesafe.ai/v1/systemone");
        request.Headers.Authorization = new("Bearer", key);
        request.Content = JsonContent.Create(new { model, state = term, questions = new { explain = new { type = "noul", instructions = "Would a short explanation of this academic term be useful to a general class audience? Judge the term, not an individual student's understanding." }, category = new { type = "choice", instructions = "Rank this candidate term for a lecture glossary.", criteria = new { high = "Specialized academic term", medium = "Domain-specific but familiar term", low = "Everyday word" } } } });
        using var ranked = await http.SendAsync(request, ct);
        if (!ranked.IsSuccessStatusCode) throw new HttpRequestException($"Jev systemone HTTP {(int)ranked.StatusCode}");
        using var result = JsonDocument.Parse(await ReadBounded(ranked, 100_000, ct));
        var answers = result.RootElement.GetProperty("answers");
        return new { term, ruleSuggestExplanation = rule, model, latencyMs = timer.ElapsedMilliseconds, jevProbability = answers.GetProperty("explain").GetProperty("noul").GetDouble(), jevRank = answers.GetProperty("category").GetProperty("choice").GetString(), jevConfidence = answers.GetProperty("category").GetProperty("confidence").GetDouble(), usage = result.RootElement.GetProperty("usage").Clone() };
    }
    public async Task<object> EvaluateSynthetic(CancellationToken ct)
    {
        var examples = new[] { (term: "phoneme", expected: true), (term: "the", expected: false), (term: "spectrogram", expected: true), (term: "today", expected: false) };
        var results = new List<object>();
        foreach (var example in examples)
        {
            var timer = Stopwatch.StartNew();
            var rule = example.term.Length >= 8 || example.term.Any(char.IsUpper);
            var ruleMs = timer.ElapsedMilliseconds;
            object? jev = null; object? gemini = null;
            if (HasJev) { try { jev = await RankTerm(example.term, ct); } catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException) { jev = new { error = ex.Message }; } }
            if (HasGemini)
            {
                timer.Restart();
                try { var response = await Agent("SyntheticTermClassifier", "Return only YES or NO: Would a concise explanation of this academic term help a general class audience?", example.term, ct, "gemini-3.5-flash-lite"); gemini = new { response, latencyMs = timer.ElapsedMilliseconds, usage = "not exposed by this agent call" }; }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException) { gemini = new { error = ex.Message }; }
            }
            results.Add(new { example.term, example.expected, rule = new { suggest = rule, latencyMs = ruleMs }, jev = jev ?? new { unavailable = "JEV_API_KEY is absent" }, gemini = gemini ?? new { unavailable = "GOOGLE_AI_STUDIO_API_KEY is absent" } });
        }
        return new { dataset = "Four labeled synthetic terms; no lecture content", results };
    }
    static async Task<byte[]> ReadBounded(HttpResponseMessage response, int limit, CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength > limit) throw new InvalidOperationException("Provider response exceeds limit");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await input.ReadAsync(buffer, ct);
            if (read == 0) return output.ToArray();
            if (output.Length + read > limit) throw new InvalidOperationException("Provider response exceeds limit");
            output.Write(buffer, 0, read);
        }
    }
}

public sealed record WebEvidence(string Kind, string Url, string Title, int StartIndex, int EndIndex);
public sealed record GroundedResult(string Answer, List<WebEvidence> Evidence, bool Inference);
