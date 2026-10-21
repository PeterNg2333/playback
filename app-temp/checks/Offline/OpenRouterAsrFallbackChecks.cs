using System.Net;
using System.Text.Json;
using Playback.Api.Audio.Asr;
using Playback.Api.Audio.Asr.Providers;

static class OpenRouterAsrFallbackChecks
{
    public static async Task Run(string path)
    {
        var request = new AsrRequest(path, "session", "microphone", 1, "hash", "en");
        Expect.That(AsrModelOptions.Fallbacks.All(x => !x.StartsWith("openai/")) && AsrModelOptions.Whisper.All(AsrModelOptions.OpenRouter.Contains) &&
            AsrModelOptions.Whisper.All(x => AsrModelOptions.Resolve(x) == x) && AsrModelOptions.Resolve(" OPENAI/whisper-large-v3 ") == "openai/whisper-large-v3",
            "Whisper must stay selectable and be sent as chosen; fallback stays non-OpenAI");
        foreach (var legacy in new[] {"openai/gpt-4o-transcribe", " OPENAI/gpt-4o-mini-transcribe "})
        {
            using var adapter = new OpenRouterAsrClient(new Handler(async (message, ct) =>
            {
                using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));
                Expect.That(body.RootElement.GetProperty("model").GetString() == AsrModelOptions.Default,
                    "An old non-Whisper OpenAI selection must resolve to Qwen before uploading");
                return new(HttpStatusCode.OK) {Content = new StringContent("{\"text\":\"Recovered\"}")};
            }), legacy);
            await adapter.Transcribe(request, default);
            await adapter.Transcribe(request with {Model = legacy}, default);
        }
        var sequence = new List<string>();
        using (var adapter = new OpenRouterAsrClient(new Handler(async (message, ct) =>
        {
            using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));
            sequence.Add(body.RootElement.GetProperty("model").GetString()!);
            return new(sequence.Count < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                {Content = new StringContent("{\"text\":\"Recovered by Gemini\"}")};
        }), AsrModelOptions.Default))
        {
            var result = await adapter.Transcribe(request, default);
            Expect.That(sequence.SequenceEqual(new[] {AsrModelOptions.Default, "elevenlabs/scribe-v2", "google/gemini-3.5-transcribe"}) &&
                result.Model?.Model == sequence[2], "Fallback must use Qwen, Scribe and Gemini with actual answering metadata");
        }
        foreach (var status in new[] {400, 404, 408, 422, 429, 502, 503})
        {
            var models = new List<string>(); var identities = new List<string>();
            using var adapter = new OpenRouterAsrClient(new Handler(async (message, ct) =>
            {
                using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));
                models.Add(body.RootElement.GetProperty("model").GetString()!);
                identities.Add(message.Headers.GetValues("Idempotency-Key").Single());
                return new((HttpStatusCode)(models.Count % 2 == 1 ? status : 200)) {Content = new StringContent("{\"text\":\"A corrected term.\"}")};
            }), "qwen/qwen3-asr-1.7b");
            var result = await adapter.Transcribe(request, default);
            await adapter.Transcribe(request, default);
            Expect.That(models.SequenceEqual(new[] {"qwen/qwen3-asr-1.7b", "elevenlabs/scribe-v2", "qwen/qwen3-asr-1.7b", "elevenlabs/scribe-v2"}) &&
                result.Model?.Model == models[1] && result.FallbackSummary!.Contains("HTTP " + status) &&
                identities[0] == identities[2] && identities[1] == identities[3] && identities[0] != identities[1],
                "Model failure must fall back with actual metadata and a stable identity per candidate");
        }
        var calls = 0;
        using (var adapter = new OpenRouterAsrClient(new Handler(async (_, ct) =>
        {
            if (++calls == 1) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new(HttpStatusCode.OK) {Content = new StringContent("{\"text\":\"Recovered\"}")};
        }), "qwen/qwen3-asr-1.7b", TimeSpan.FromMilliseconds(20)))
        {
            var result = await adapter.Transcribe(request, default);
            Expect.That(calls == 2 && result.FallbackSummary!.Contains("timeout"), "A model timeout must admit the next candidate");
        }
        foreach (var status in new[] {401, 402, 403, 413})
        {
            calls = 0;
            using var adapter = new OpenRouterAsrClient(new Handler((_, _) =>
            { calls++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)); }), "qwen/qwen3-asr-1.7b");
            try { await adapter.Transcribe(request, default); throw new CheckFailed("Global ASR error was swallowed"); }
            catch (HttpRequestException ex) { Expect.That((int)ex.StatusCode! == status && calls == 1, "Global credential/billing/size errors must stop without model retries"); }
        }
        calls = 0;
        using (var adapter = new OpenRouterAsrClient(new Handler((_, _) =>
        { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); }), "qwen/qwen3-asr-1.7b"))
        {
            try { await adapter.Transcribe(request, default); throw new CheckFailed("Exhausted fallback appeared successful"); }
            catch (HttpRequestException) { Expect.That(calls == 3, "ASR fallback must be bounded to three distinct models"); }
        }
        calls = 0;
        using (var cancel = new CancellationTokenSource())
        using (var adapter = new OpenRouterAsrClient(new Handler((_, ct) =>
        { calls++; cancel.Cancel(); ct.ThrowIfCancellationRequested(); throw new CheckFailed("Cancellation ignored"); }), "qwen/qwen3-asr-1.7b"))
        {
            try { await adapter.Transcribe(request, cancel.Token); throw new CheckFailed("Caller cancellation swallowed"); }
            catch (OperationCanceledException) { Expect.That(calls == 1, "Caller cancellation must not upload to another model"); }
        }
    }
    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }
}
