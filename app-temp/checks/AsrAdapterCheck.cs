using System.Net;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using Playback.Api.Services.Audio;

internal static class AsrAdapterCheck
{
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static async Task Run()
    {
        Require(AsrAdapters.ResolveProvider(null, "your_openrouter_api_key_here") == "sensevoice" &&
            AsrAdapters.ResolveProvider(null, "fixture-key") == "openrouter" &&
            AsrAdapters.ResolveProvider("sensevoice", "fixture-key") == "sensevoice",
            "ASR selection must respect explicit configuration and ignore placeholder keys");
        try { AsrAdapters.ResolveProvider("typo", null); throw new Exception("Invalid provider accepted"); }
        catch (InvalidOperationException) { }

        var oldKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        var oldOffline = Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST");
        var path = Path.Combine(Path.GetTempPath(), $"playback-adapter-{Guid.NewGuid():N}.wav");
        try
        {
            using (var writer = new WaveFileWriter(path, new WaveFormat(16_000, 16, 1)))
                writer.Write(new byte[3200], 0, 3200);
            // All HTTP calls below are intercepted in memory. No external service is contacted.
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "fixture-key");
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", null);
            var identities = new List<string>();
            var languages = new List<string?>();
            var handler = new StubHandler(async (message, ct) =>
            {
                Require(message.RequestUri?.AbsoluteUri == "https://openrouter.ai/api/v1/audio/transcriptions" &&
                    message.Method == HttpMethod.Post, "ASR must use the fixed transcription endpoint");
                Require(message.Headers.Authorization?.Scheme == "Bearer" &&
                    message.Headers.Authorization.Parameter == "fixture-key", "ASR must use process credentials");
                identities.Add(message.Headers.GetValues("Idempotency-Key").Single());
                using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));
                var root = body.RootElement;
                languages.Add(root.TryGetProperty("language", out var language) ? language.GetString() : null);
                Require(root.GetProperty("model").GetString() == (languages.Count == 8 ? "openai/whisper-large-v3-turbo" : "qwen/qwen3-asr-1.7b") &&
                    root.GetProperty("response_format").GetString() == "json" &&
                    root.GetProperty("input_audio").GetProperty("format").GetString() == "wav",
                    "OpenRouter ASR request contract mismatch");
                var bytes = Convert.FromBase64String(root.GetProperty("input_audio").GetProperty("data").GetString()!);
                using var wav = new WaveFileReader(new MemoryStream(bytes));
                Require(wav.WaveFormat.SampleRate == 16_000 && wav.WaveFormat.Channels == 1 && wav.WaveFormat.BitsPerSample == 16,
                    "Adapter must send normalized PCM WAV");
                return Reply("{\"text\":\"今日研究 FFT.\",\"usage\":{\"seconds\":0.1}}");
            });
            using var adapter = new OpenRouterAsrClient(handler, "qwen/qwen3-asr-1.7b");
            var request = new AsrRequest(path, "session", "microphone", 1, "hash");
            var result = await adapter.Transcribe(request, CancellationToken.None);
            Require(result.Text == "今日研究 FFT." && result.DurationSeconds == 0.1 && result.InferenceSeconds is null && result.UsageJson == "{\"seconds\":0.1}",
                "REST adapter must preserve original text and never invent provider metrics");
            await adapter.Transcribe(request, CancellationToken.None);
            await adapter.Transcribe(request with { SourceId = "system" }, CancellationToken.None);
            Require(identities[0] == identities[1] && identities[1] != identities[2],
                "Retries must retain session/source/sequence/hash identity");
            foreach (var language in new[] { "yue", "zh", "en" })
                await adapter.Transcribe(request with { Language = language }, CancellationToken.None);
            Require(languages.SequenceEqual(new string?[] { null, null, null, "yue", "zh", "en" }) &&
                identities[0] != identities[3] && identities[3] != identities[4],
                "Auto must omit the language hint; explicit dialects must be sent and change retry identity");
            await adapter.Transcribe(request with { Language = "yue-en" }, CancellationToken.None);
            var selected = await adapter.Transcribe(request with { Model = "openai/whisper-large-v3-turbo" }, CancellationToken.None);
            Require(languages[6] is null && selected.Model?.Model == "openai/whisper-large-v3-turbo" && identities[7] != identities[0],
                "Mixed language must omit a single-language hint and selected model must reach the provider and saved metadata");
            Require(OpenRouterAsrClient.ParseResponse(Encoding.UTF8.GetBytes("{\"text\":\"\"}")).Text == "",
                "Empty ASR output must not become invented speech");
            try { OpenRouterAsrClient.ParseResponse(Encoding.UTF8.GetBytes("{\"status\":\"ok\"}")); throw new Exception("Bad schema accepted"); }
            catch (InvalidOperationException) { }

            using var failure = new OpenRouterAsrClient(new StubHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("sensitive provider body") })), "other/asr");
            try { await failure.Transcribe(request, CancellationToken.None); throw new Exception("HTTP failure accepted"); }
            catch (HttpRequestException ex)
            {
                Require(ex.StatusCode == HttpStatusCode.Unauthorized && ex.Message == "OpenRouter ASR HTTP 401",
                    "Provider failures must surface without reflecting credentials or response bodies");
            }
            using var large = new OpenRouterAsrClient(new StubHandler((_, _) => Task.FromResult(Reply(new string('x', 512_001)))), "other/asr");
            try { await large.Transcribe(request, CancellationToken.None); throw new Exception("Oversized response accepted"); }
            catch (InvalidOperationException ex) { Require(ex.Message.Contains("exceeds limit"), "Response must be bounded"); }

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try { await adapter.Transcribe(request, canceled.Token); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { }
            var calls = identities.Count;
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "yes");
            try { await adapter.Transcribe(request, CancellationToken.None); throw new Exception("Offline ASR accepted"); }
            catch (InvalidOperationException ex) { Require(ex.Message.Contains("offline"), "Offline mode must reject provider calls"); }
            Require(identities.Count == calls, "Offline checks must not upload audio");
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", null);
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "your_openrouter_api_key_here");
            try { await adapter.Transcribe(request, CancellationToken.None); throw new Exception("Placeholder key accepted"); }
            catch (InvalidOperationException ex) { Require(ex.Message.Contains("unavailable"), "Missing key must fail explicitly"); }
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", oldKey);
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", oldOffline);
            File.Delete(path);
        }
        Console.WriteLine("ASR adapter checks passed (in-memory HTTP only): request, normalization, retry identity, errors, limits, cancellation, offline guard");
    }

    static HttpResponseMessage Reply(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }
}
