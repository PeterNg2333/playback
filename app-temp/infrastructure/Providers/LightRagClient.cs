using System.Net.Http.Json;
using System.Text.Json;
using Playback.Api.Ask;
using Playback.Domain.Evidence;

namespace Playback.Api.Providers;

// The concept graph sidecar (app-temp/graph): a fixed loopback endpoint, no redirects, bounded time and response size.
// Off unless PLAYBACK_CONCEPT_GRAPH=yes; refused in offline tests. Session IDs and document IDs are the only identifiers sent.
public sealed class LightRagClient : IConceptGraph, IDisposable
{
    public const int ResponseLimit = 2_000_000;
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    readonly HttpClient http;

    public LightRagClient(HttpMessageHandler? handler = null)
    {
        var port = int.TryParse(Environment.GetEnvironmentVariable("PLAYBACK_GRAPH_PORT"), out var value) && value is > 1024 and < 65536 ? value : 5090;
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(300) };
    }

    public bool Enabled => !PlaybackEnvironment.Offline && Environment.GetEnvironmentVariable("PLAYBACK_CONCEPT_GRAPH") == "yes";

    void Require(string sessionId)
    {
        if (PlaybackEnvironment.Offline) throw new InvalidOperationException("The concept graph is disabled for offline tests");
        if (!Enabled) throw new InvalidOperationException("The concept graph is off; set PLAYBACK_CONCEPT_GRAPH=yes and start the sidecar");
        if (!ChunkIdentity.IsSessionId(sessionId)) throw new InvalidOperationException("Invalid session identity");
    }

    async Task<T> Send<T>(HttpMethod method, string path, object? body, TimeSpan deadline, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(deadline);
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body, options: Json) };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if ((int)response.StatusCode is >= 300 and < 400) throw new InvalidOperationException("Concept graph redirect refused");
        var bytes = await ProviderResponseReader.ReadBounded(response, ResponseLimit, timeout.Token);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Concept graph HTTP {(int)response.StatusCode}");
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidOperationException("Concept graph returned no data");
    }

    public async Task<ConceptUpsert> Upsert(string sessionId, string language, IReadOnlyList<ConceptDocument> documents, CancellationToken ct)
    {
        Require(sessionId);
        // Extraction runs entity and relation prompts per document; a batch of 16 minutes can take a while.
        return await Send<ConceptUpsert>(HttpMethod.Put, $"sessions/{sessionId}/docs", new
        {
            language = Language(language),
            docs = documents.Select(x => new { x.Id, x.Text, x.Hash })
        }, TimeSpan.FromSeconds(280), ct);
    }

    public async Task Delete(string sessionId, IReadOnlyList<string> documentIds, CancellationToken ct)
    {
        Require(sessionId);
        await Send<JsonElement>(HttpMethod.Post, $"sessions/{sessionId}/docs/delete", new { ids = documentIds }, TimeSpan.FromSeconds(120), ct);
    }

    public async Task<ConceptResult> Query(string sessionId, string question, CancellationToken ct)
    {
        Require(sessionId);
        return await Send<ConceptResult>(HttpMethod.Post, $"sessions/{sessionId}/query", new { question = question[..Math.Min(question.Length, 1000)] },
            TimeSpan.FromSeconds(30), ct);
    }

    public async Task<ConceptResult> Graph(string sessionId, int limit, CancellationToken ct)
    {
        Require(sessionId);
        return await Send<ConceptResult>(HttpMethod.Get, $"sessions/{sessionId}/graph?limit={Math.Clamp(limit, 1, 500)}", null, TimeSpan.FromSeconds(30), ct);
    }

    // The output language LightRAG writes entity descriptions in follows the session's note language.
    static string Language(string noteLanguage) => noteLanguage switch
    {
        "zh-Hant" => "Traditional Chinese (Hong Kong)",
        "zh-Hans" => "Simplified Chinese",
        _ => "English"
    };

    public void Dispose() => http.Dispose();
}
