using System.Text.Json;
using System.Text.RegularExpressions;
using Playback.Api.Services;

namespace Playback.Api.Services.Audio;

public sealed partial class SenseVoiceClient
{
    [GeneratedRegex(@"<\|[^|>]*\|>")]
    private static partial Regex SpecialTokenRegex();

    readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(120) };

    public async Task<AsrResult> Transcribe(string path, CancellationToken ct)
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST") == "yes")
            throw new InvalidOperationException("External ASR is disabled for offline tests");
        var endpoint = Environment.GetEnvironmentVariable("PLAYBACK_ASR_ENDPOINT") ?? "https://dev-aks.setsailapi.com/stt/infer/upload";
        if (endpoint != "https://dev-aks.setsailapi.com/stt/infer/upload") throw new InvalidOperationException("ASR endpoint is not allowlisted");
        if (new FileInfo(path).Length is < 44 or > 25_000_000) throw new InvalidOperationException("ASR WAV must be 44 bytes to 25 MB");
        var wav = AsrAudioPreparer.Prepare(path, ct);
        using var body = new MultipartFormDataContent();
        using var content = new ByteArrayContent(wav);
        content.Headers.ContentType = new("audio/wav");
        body.Add(content, "file", "audio.wav");
        using var response = await http.PostAsync(endpoint, body, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"SenseVoice HTTP {(int)response.StatusCode}");
        var bytes = await ProviderResponseReader.ReadBounded(response, 512_000, ct);
        return ParseResponse(bytes);
    }
    public static AsrResult ParseResponse(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;

        double? GetDouble(string name) => root.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number
            ? field.GetDouble() : null;
        string? GetString(string name) => root.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String
            ? field.GetString() : null;

        string? text = GetString("raw") is string raw
            ? SpecialTokenRegex().Replace(raw, "").Trim()
            : GetString("text");
        if (text is null)
            throw new InvalidOperationException("SenseVoice response schema is unrecognized");

        return new AsrResult(text,
            GetDouble("duration_seconds"),
            GetDouble("inference_time_seconds"),
            GetString("language"),
            GetDouble("rtf"));
    }
}

public sealed record AsrResult(
    string Text,
    double? DurationSeconds,
    double? InferenceSeconds,
    string? Language,
    double? RealTimeFactor);
