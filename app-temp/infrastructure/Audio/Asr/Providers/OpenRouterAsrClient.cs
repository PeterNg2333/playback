using Playback.Api.Providers;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Playback.Api.Audio.Asr.Providers;

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
        Model = new("openrouter", model, "rest", SupportsLanguageHint: true);
        http = new(handler) { Timeout = TimeSpan.FromSeconds(70) };
    }

    public async Task<AsrResult> Transcribe(AsrRequest request, CancellationToken ct)
    {
        LanguageSettings.ValidateAsr(request.Language);
        AsrModelOptions.Validate(request.Model);
        var model = request.Model ?? Model.Model;
        if (PlaybackEnvironment.Offline)
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
        var identity = JsonSerializer.Serialize(new { request.SessionId, request.SourceId, request.Sequence, request.Hash, request.Language, Model = model });
        message.Headers.TryAddWithoutValidation("Idempotency-Key",
            ContentHash.Of(identity));
        var payload = new Dictionary<string, object>
        {
            ["model"] = model,
            ["input_audio"] = new { data = Convert.ToBase64String(wav), format = "wav" },
            ["response_format"] = "json"
        };
        if (LanguageSettings.ProviderHint(request.Language) is { } hint) payload["language"] = hint;
        message.Content = JsonContent.Create(payload);
        using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenRouter ASR HTTP {(int)response.StatusCode}", null, response.StatusCode);
        return ParseResponse(await ProviderResponseReader.ReadBounded(response, 512_000, ct)) with { Model = Model with { Model = model } };
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
        return new(text.GetString()!, duration, null, language, null,
            UsageJson: root.TryGetProperty("usage", out var providerUsage) && providerUsage.ValueKind == JsonValueKind.Object ? providerUsage.GetRawText() : null);
    }

    public void Dispose() => http.Dispose();
}
