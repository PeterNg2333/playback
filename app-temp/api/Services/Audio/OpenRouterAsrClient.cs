using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Playback.Api.Services.Audio;

public sealed class OpenRouterAsrClient : IAsrAdapter, IDisposable
{
    readonly HttpClient http;
    public AsrModel Model { get; }

    public OpenRouterAsrClient() : this(new HttpClientHandler { AllowAutoRedirect = false },
        Environment.GetEnvironmentVariable("PLAYBACK_ASR_MODEL") ?? "qwen/qwen3-asr-1.7b") { }

    public OpenRouterAsrClient(HttpMessageHandler handler, string model)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Length > 200 || model.Any(char.IsControl))
            throw new InvalidOperationException("An OpenRouter transcription model ID is required");
        Model = new("openrouter", model, "rest");
        http = new(handler) { Timeout = TimeSpan.FromSeconds(70) };
    }

    public async Task<AsrResult> Transcribe(AsrRequest request, CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST") == "yes")
            throw new InvalidOperationException("External ASR is disabled for offline tests");
        var key = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        if (!AsrAdapters.HasOpenRouterKey(key))
            throw new InvalidOperationException("OPENROUTER_API_KEY is unavailable");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(70));
        ct = deadline.Token;
        if (new FileInfo(request.Path).Length is < 44 or > 25_000_000)
            throw new InvalidOperationException("ASR WAV must be 44 bytes to 25 MB");
        var wav = AsrAudioPreparer.Prepare(request.Path, ct);
        using var message = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/audio/transcriptions");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        // Stable retry identity; remote deduplication is provider-dependent. Local chunk IDs
        // remain authoritative and only one completed transcript is saved for each chunk.
        var identity = JsonSerializer.Serialize(new { request.SessionId, request.SourceId, request.Sequence, request.Hash });
        message.Headers.TryAddWithoutValidation("Idempotency-Key",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant());
        message.Content = JsonContent.Create(new
        {
            model = Model.Model,
            input_audio = new { data = Convert.ToBase64String(wav), format = "wav" },
            response_format = "json"
        });
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenRouter ASR HTTP {(int)response.StatusCode}", null, response.StatusCode);
        return ParseResponse(await ProviderResponseReader.ReadBounded(response, 512_000, ct));
    }

    public static AsrResult ParseResponse(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException("OpenRouter ASR response schema is unrecognized");
        double? duration = root.TryGetProperty("duration", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
        if (duration is null && root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object &&
            usage.TryGetProperty("seconds", out value) && value.ValueKind == JsonValueKind.Number)
            duration = value.GetDouble();
        var language = root.TryGetProperty("language", out var lang) && lang.ValueKind == JsonValueKind.String ? lang.GetString() : null;
        return new(text.GetString()!, duration, null, language, null);
    }

    public void Dispose() => http.Dispose();
}
