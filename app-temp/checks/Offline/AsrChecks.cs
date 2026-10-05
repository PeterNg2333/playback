using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NAudio.Wave;
using Playback.Api.Audio.Asr;
using Playback.Api.Audio.Asr.Providers;
using Playback.Api.Db;

// ASR: which provider is chosen, what each adapter sends and reads back (over in-memory HTTP), how
// captured audio is prepared, which chunks are never uploaded, and which text needs review.
static class AsrChecks
{
    public static async Task Run()
    {
        ProviderChoice();
        await OpenRouterAdapter();
        SenseVoiceResponses();
        AudioPreparation();
        DigitalSilence();
        Expect.That(AsrProcessor.NetworkPermissionDenied(new HttpRequestException("Connection failed", new SocketException((int)SocketError.AccessDenied))),
            "A denied REST connection must stop automatic ASR retries");
        Expect.That(!AsrProcessor.NetworkPermissionDenied(new HttpRequestException("Service unavailable")),
            "Transient HTTP failures must remain retryable");
        Expect.That(Transcript.NeedsReview("The term was [unclear].") && !Transcript.NeedsReview("") && !Transcript.NeedsReview("This is unclear but audible."),
            "Only explicit uncertainty markers should trigger human review; empty ASR must not masquerade as confidence");
        Console.WriteLine("ASR checks passed (in-memory HTTP only): provider choice, request contract, retry identity, errors, limits, cancellation, offline guard, " +
            "SenseVoice parsing, 16 kHz mono preparation, digital silence, uncertainty markers");
    }

    static void ProviderChoice()
    {
        Expect.That(AsrAdapters.ResolveProvider(null, "your_openrouter_api_key_here") == "sensevoice" &&
            AsrAdapters.ResolveProvider(null, "fixture-key") == "openrouter" &&
            AsrAdapters.ResolveProvider("sensevoice", "fixture-key") == "sensevoice",
            "ASR selection must respect explicit configuration and ignore placeholder keys");
        Expect.Rejects(() => AsrAdapters.ResolveProvider("typo", null), "Invalid provider accepted");
    }

    static async Task OpenRouterAdapter()
    {
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
                Expect.That(message.RequestUri?.AbsoluteUri == "https://openrouter.ai/api/v1/audio/transcriptions" &&
                    message.Method == HttpMethod.Post, "ASR must use the fixed transcription endpoint");
                Expect.That(message.Headers.Authorization?.Scheme == "Bearer" &&
                    message.Headers.Authorization.Parameter == "fixture-key", "ASR must use process credentials");
                identities.Add(message.Headers.GetValues("Idempotency-Key").Single());
                using var body = JsonDocument.Parse(await message.Content!.ReadAsStringAsync(ct));
                var root = body.RootElement;
                languages.Add(root.TryGetProperty("language", out var language) ? language.GetString() : null);
                Expect.That(root.GetProperty("model").GetString() == (languages.Count == 8 ? "openai/whisper-large-v3-turbo" : "qwen/qwen3-asr-1.7b") &&
                    root.GetProperty("response_format").GetString() == "json" &&
                    root.GetProperty("input_audio").GetProperty("format").GetString() == "wav",
                    "OpenRouter ASR request contract mismatch");
                var bytes = Convert.FromBase64String(root.GetProperty("input_audio").GetProperty("data").GetString()!);
                using var wav = new WaveFileReader(new MemoryStream(bytes));
                Expect.That(wav.WaveFormat.SampleRate == 16_000 && wav.WaveFormat.Channels == 1 && wav.WaveFormat.BitsPerSample == 16,
                    "Adapter must send normalized PCM WAV");
                return Reply("{\"text\":\"今日研究 FFT.\",\"usage\":{\"seconds\":0.1}}");
            });
            using var adapter = new OpenRouterAsrClient(handler, "qwen/qwen3-asr-1.7b");
            var request = new AsrRequest(path, "session", "microphone", 1, "hash");
            var result = await adapter.Transcribe(request, CancellationToken.None);
            Expect.That(result.Text == "今日研究 FFT." && result.DurationSeconds == 0.1 && result.InferenceSeconds is null && result.UsageJson == "{\"seconds\":0.1}",
                "REST adapter must preserve original text and never invent provider metrics");
            await adapter.Transcribe(request, CancellationToken.None);
            await adapter.Transcribe(request with { SourceId = "system" }, CancellationToken.None);
            Expect.That(identities[0] == identities[1] && identities[1] != identities[2],
                "Retries must retain session/source/sequence/hash identity");
            foreach (var language in new[] { "yue", "zh", "en" })
                await adapter.Transcribe(request with { Language = language }, CancellationToken.None);
            Expect.That(languages.SequenceEqual(new string?[] { null, null, null, "yue", "zh", "en" }) &&
                identities[0] != identities[3] && identities[3] != identities[4],
                "Auto must omit the language hint; explicit dialects must be sent and change retry identity");
            await adapter.Transcribe(request with { Language = "yue-en" }, CancellationToken.None);
            var selected = await adapter.Transcribe(request with { Model = "openai/whisper-large-v3-turbo" }, CancellationToken.None);
            Expect.That(languages[6] is null && selected.Model?.Model == "openai/whisper-large-v3-turbo" && identities[7] != identities[0],
                "Mixed language must omit a single-language hint and selected model must reach the provider and saved metadata");
            Expect.That(OpenRouterAsrClient.ParseResponse(Encoding.UTF8.GetBytes("{\"text\":\"\"}")).Text == "",
                "Empty ASR output must not become invented speech");
            Expect.Rejects(() => OpenRouterAsrClient.ParseResponse(Encoding.UTF8.GetBytes("{\"status\":\"ok\"}")), "Bad schema accepted");

            using var failure = new OpenRouterAsrClient(new StubHandler((_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("sensitive provider body") })), "other/asr");
            try
            {
                await failure.Transcribe(request, CancellationToken.None);
                throw new CheckFailed("HTTP failure accepted");
            }
            catch (HttpRequestException ex)
            {
                Expect.That(ex.StatusCode == HttpStatusCode.Unauthorized && ex.Message == "OpenRouter ASR HTTP 401",
                    "Provider failures must surface without reflecting credentials or response bodies");
            }
            using var large = new OpenRouterAsrClient(new StubHandler((_, _) => Task.FromResult(Reply(new string('x', 512_001)))), "other/asr");
            try
            {
                await large.Transcribe(request, CancellationToken.None);
                throw new CheckFailed("Oversized response accepted");
            }
            catch (InvalidOperationException ex) { Expect.That(ex.Message.Contains("exceeds limit"), "Response must be bounded"); }

            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            try
            {
                await adapter.Transcribe(request, canceled.Token);
                throw new CheckFailed("Cancellation ignored");
            }
            catch (OperationCanceledException) { }
            var calls = identities.Count;
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "yes");
            try
            {
                await adapter.Transcribe(request, CancellationToken.None);
                throw new CheckFailed("Offline ASR accepted");
            }
            catch (InvalidOperationException ex) { Expect.That(ex.Message.Contains("offline"), "Offline mode must reject provider calls"); }
            Expect.That(identities.Count == calls, "Offline checks must not upload audio");
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", null);
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "your_openrouter_api_key_here");
            try
            {
                await adapter.Transcribe(request, CancellationToken.None);
                throw new CheckFailed("Placeholder key accepted");
            }
            catch (InvalidOperationException ex) { Expect.That(ex.Message.Contains("unavailable"), "Missing key must fail explicitly"); }
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", oldKey);
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", oldOffline);
            File.Delete(path);
        }
    }

    static void SenseVoiceResponses()
    {
        Expect.That(SenseVoiceClient.ParseResponse(Json("{\"raw\":\"exact [unclear]\",\"text\":\"model revision\"}")).Text == "exact [unclear]",
            "ASR original must win over a processed text field");
        Expect.That(SenseVoiceClient.ParseResponse(Json("{\"raw\":\"\"}")).Text == "", "Silent audio must stay uncertain, not become invented text");
        var asr = SenseVoiceClient.ParseResponse(Json(
            "{\"duration_seconds\":10,\"inference_time_seconds\":1.5,\"language\":\"en_US\",\"raw\":\"<|en|><|NEUTRAL|><|Speech|><|withitn|>Hello class.\",\"rtf\":0.15}"));
        Expect.That(asr.Text == "Hello class.", "SenseVoice control tokens must not appear in transcript text");
        Expect.That(asr.DurationSeconds == 10 && asr.InferenceSeconds == 1.5 && asr.RealTimeFactor == 0.15 && asr.Language == "en_US",
            "SenseVoice timing and language metadata must be mapped");
        Expect.That(SenseVoiceClient.ParseResponse(Json("{\"raw\":\"<|en|><|NEUTRAL|><|Speech|><|withitn|>\"}")).Text == "",
            "Metadata-only responses must not become transcript text");
        Expect.Rejects(() => SenseVoiceClient.ParseResponse(Json("{\"status\":\"ok\"}")), "Unknown ASR schema was accepted");
    }

    // System audio is 48 kHz stereo float; the provider receives compact 16 kHz mono PCM, the original stays.
    static void AudioPreparation()
    {
        var stereoPath = Path.Combine(Path.GetTempPath(), $"playback-asr-{Guid.NewGuid():N}.wav");
        try
        {
            using (var writer = new WaveFileWriter(stereoPath, WaveFormat.CreateIeeeFloatWaveFormat(48_000, 2)))
            {
                var stereo = new float[48_000 * 2];
                Array.Fill(stereo, 0.5f);
                var bytes = new byte[stereo.Length * sizeof(float)];
                Buffer.BlockCopy(stereo, 0, bytes, 0, bytes.Length);
                writer.Write(bytes, 0, bytes.Length);
            }
            var originalLength = new FileInfo(stereoPath).Length;
            var prepared = AsrAudioPreparer.Prepare(stereoPath, CancellationToken.None);
            using var normalized = new WaveFileReader(new MemoryStream(prepared));
            var sample = new byte[2];
            normalized.CurrentTime = TimeSpan.FromMilliseconds(500);
            normalized.ReadExactly(sample);
            Expect.That(normalized.WaveFormat.SampleRate == 16_000 && normalized.WaveFormat.Channels == 1 &&
                normalized.WaveFormat.BitsPerSample == 16 && prepared.Length < originalLength / 4 &&
                Math.Abs(BitConverter.ToInt16(sample) - 16384) < 250,
                $"48 kHz stereo system audio must become compact 16 kHz mono PCM for ASR: {normalized.WaveFormat}, {prepared.Length}/{originalLength} bytes, sample {BitConverter.ToInt16(sample)}");
            Expect.That(new FileInfo(stereoPath).Length == originalLength, "ASR preparation must preserve the original recording");
        }
        finally { File.Delete(stereoPath); }
    }

    static void DigitalSilence()
    {
        var silentWav = Path.Combine(Path.GetTempPath(), $"playback-silent-{Guid.NewGuid():N}.wav");
        var toneWav = Path.Combine(Path.GetTempPath(), $"playback-tone-{Guid.NewGuid():N}.wav");
        try
        {
            var wav = new byte[44 + 32000];
            Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
            BitConverter.GetBytes(wav.Length - 8).CopyTo(wav, 4);
            Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(wav, 8);
            BitConverter.GetBytes(16).CopyTo(wav, 16);
            BitConverter.GetBytes((short)1).CopyTo(wav, 20);
            BitConverter.GetBytes((short)1).CopyTo(wav, 22);
            BitConverter.GetBytes(16000).CopyTo(wav, 24);
            BitConverter.GetBytes(32000).CopyTo(wav, 28);
            BitConverter.GetBytes((short)2).CopyTo(wav, 32);
            BitConverter.GetBytes((short)16).CopyTo(wav, 34);
            Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
            BitConverter.GetBytes(32000).CopyTo(wav, 40);
            File.WriteAllBytes(silentWav, wav);
            Expect.That(AudioSilence.IsDigitalSilence(silentWav), "Digital silence should skip remote ASR");
            wav[44] = 1;
            File.WriteAllBytes(toneWav, wav);
            Expect.That(!AudioSilence.IsDigitalSilence(toneWav), "Nonzero audio must not be skipped as silence");
        }
        finally
        {
            File.Delete(silentWav);
            File.Delete(toneWav);
        }
    }

    static byte[] Json(string value) => Encoding.UTF8.GetBytes(value);

    static HttpResponseMessage Reply(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }
}
