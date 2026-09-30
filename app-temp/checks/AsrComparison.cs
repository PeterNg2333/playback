using System.Diagnostics;
using System.Security.Cryptography;
using Playback.Api.Services.Audio;

internal static class AsrComparison
{
    public static async Task Run(bool live)
    {
        var folder = Path.GetFullPath("app-temp/data/test-audio/sampleAudio");
        var chunks = SampleAudioPreview.Chunks(folder).Take(2).ToArray();
        Console.WriteLine("ASR comparison: first two 30-second chunks of the repository Week 3 sample; no database changes.");
        if (!live)
        {
            Console.WriteLine("Offline preparation passed. Add --live to upload these two chunks twice to BOTH SenseVoice and OpenRouter (8 requests total).");
            return;
        }
        if (Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST") == "yes")
            throw new InvalidOperationException("Remove PLAYBACK_OFFLINE_TEST to run the explicitly requested live comparison");
        if (!AsrAdapters.HasOpenRouterKey(Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")))
            throw new InvalidOperationException("A real OPENROUTER_API_KEY is required for the live comparison");
        using var senseVoice = new SenseVoiceClient();
        using var openRouter = new OpenRouterAsrClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var runId = Guid.NewGuid().ToString("N");
        for (var round = 0; round < 2; round++)
            foreach (var (sequence, wav, _) in chunks)
            {
                var path = Path.Combine(Path.GetTempPath(), $"playback-asr-compare-{runId}-{sequence}.wav");
                try
                {
                    await File.WriteAllBytesAsync(path, wav, deadline.Token);
                    var request = new AsrRequest(path, $"{runId}-round-{round}", "sample-system", sequence,
                        Convert.ToHexString(SHA256.HashData(wav)).ToLowerInvariant());
                    IAsrAdapter[] adapters = round == 0 ? [senseVoice, openRouter] : [openRouter, senseVoice];
                    foreach (var adapter in adapters)
                    {
                        var timer = Stopwatch.StartNew();
                        try
                        {
                            var result = await adapter.Transcribe(request, deadline.Token);
                            Console.WriteLine($"round={round + 1} chunk={sequence} provider={adapter.Model.Provider} model={adapter.Model.Model} " +
                                $"elapsed_ms={timer.ElapsedMilliseconds} latency_per_audio_second={timer.Elapsed.TotalSeconds / 30:F3} " +
                                $"text_chars={result.Text.Length} inference_seconds={result.InferenceSeconds?.ToString("F3") ?? "unreported"}");
                        }
                        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException)
                        {
                            Environment.ExitCode = 1;
                            // Do not print provider bodies, lecture text, or credentials.
                            Console.WriteLine($"round={round + 1} chunk={sequence} provider={adapter.Model.Provider} elapsed_ms={timer.ElapsedMilliseconds} " +
                                $"failed={ex.GetType().Name} http_status={(ex as HttpRequestException)?.StatusCode?.ToString() ?? "none"}");
                            deadline.Token.ThrowIfCancellationRequested();
                        }
                    }
                }
                finally { File.Delete(path); }
            }
        Console.WriteLine("Latency comparison finished. Text length does not measure accuracy; no transcript text was printed or saved.");
    }
}
