using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Playback.Application.Audio;
using Playback.Infrastructure.Audio;

static class AudioProjectionChecks
{
    public static async Task Run()
    {
        var folder = Path.Combine("output", "audio-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var session = new string('a', 32);
        var paths = new Dictionary<string, string>();
        var parts = new List<AudioPart>();
        var payloads = new List<byte[]>();
        for (var i = 0; i < 3; i++)
        {
            var payload = Enumerable.Range(0, 32000).Select(x => (byte)(x + i)).ToArray(); payloads.Add(payload);
            var path = Path.Combine(folder, $"original-{i}.wav"); paths.Add($"child-{i}", path);
            using (var stream = File.Create(path)) using (var writer = new BinaryWriter(stream))
            {
                writer.Write("RIFF"u8); writer.Write(36 + payload.Length); writer.Write("WAVEfmt "u8); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000); writer.Write((short)2); writer.Write((short)16);
                writer.Write("data"u8); writer.Write(payload.Length); writer.Write(payload);
            }
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            parts.Add(new($"child-{i}", session, "microphone", "stream", i, hash, i * 1000, (i + 1) * 1000, "vad-silence", null, true));
        }
        var files = new WavCombinedAudioFiles(folder, id => Task.FromResult(paths.GetValueOrDefault(id)));
        var repo = new Repository { Items = parts };
        var workflow = new CombinedAudioWorkflow(repo, files);
        await workflow.Run(session, true, 60, "policy", default);
        var published = repo.Segments.Values.Single();
        Expect.That(published.State == "published" && published.Children.Select(x => x.FrameOffset).SequenceEqual(new long[] { 0, 16000, 32000 }), "Child sample offsets missing");
        var combined = File.ReadAllBytes(files.PathFor(session, published.Id));
        Expect.That(combined.AsSpan(44).SequenceEqual(payloads.SelectMany(x => x).ToArray()) && BitConverter.ToInt32(combined, 4) == combined.Length - 8, "Combined WAV lost samples or has invalid header");
        var writes = repo.Writes; await workflow.Run(session, true, 60, "policy", default);
        Expect.That(repo.Writes == writes, "Idempotent retry republished an intact segment");
        File.WriteAllBytes(files.PathFor(session, published.Id), [1, 2]);
        await workflow.Run(session, true, 60, "policy", default);
        Expect.That(await files.Valid(repo.Segments.Values.Single(), default), "Corrupt derived file did not recover");
        repo.Segments.Clear(); repo.FailPublicationOnce = true;
        try { await workflow.Run(session, true, 60, "policy", default); throw new CheckFailed("Crash fixture did not fail"); }
        catch (IOException) { }
        Expect.That(repo.Segments.Values.Single().State != "published", "Failed publication exposed a completed manifest");
        await workflow.Run(session, true, 60, "policy", default);
        Expect.That(repo.Segments.Count == 1 && await files.Valid(repo.Segments.Values.Single(), default), "Recovery duplicated or lost segment");
        foreach (var part in parts) Expect.That(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(paths[part.Id]))).ToLowerInvariant() == part.OriginalHash, "An original WAV was modified");
        var inspected = await files.Inspect(parts[0].Id, default);
        var ready = parts.Select(x => x with { Format = inspected.Format, Frames = inspected.Frames }).ToList();
        Expect.That(QuietAudio.Plan(ready.Select(x => x with { StreamClosed = false }), 60, "r").Count == 0, "Growing quiet tail was published");
        foreach (var changed in new[] { ready[1] with { Status = "asr-empty" }, ready[1] with { StreamId = "resume" }, ready[1] with { StartMs = 1001 }, ready[1] with { SourceId = "system" }, ready[1] with { Format = inspected.Format with { Channels = 2 } } })
        {
            var plan = QuietAudio.Plan([ready[0], changed, ready[2]], 60, "r");
            Expect.That(plan.All(x => x.Children.Count < 3), "Speech, pause, gap, source or format boundary was crossed");
        }
        var longQuiet = Enumerable.Range(0, 20).Select(i => ready[0] with { Id = "q" + i, Sequence = i, StartMs = i * 1000, EndMs = (i + 1) * 1000 });
        Expect.That(QuietAudio.Plan(longQuiet, 10, "r").Select(x => x.Children.Count).SequenceEqual(new[] { 10, 10 }), "Maximum combined duration not enforced");
        Expect.That(!SpeechBoundaryPolicy.Ready([new("mic", 0, 2000), new("system", 1000, 2000)], 2000, 2, -1), "Dual-source speech ended too early");
        Expect.That(SpeechBoundaryPolicy.Ready([new("mic", 0, 3000), new("system", 1000, 3000)], 3000, 2, -1), "Dual-source quiet boundary missing");
        Expect.That(!SpeechBoundaryPolicy.Ready([new("mic", 0, 1000)], 3000, 2, -1), "Stale capture established silence");
        Console.WriteLine("Audio projection checks passed: valid streamed WAV/sample offsets, immutable originals, duration/source/format/pause/gap splitting, idempotent crash/corruption recovery, dual-source boundary");
    }
    sealed class Repository : ICombinedAudioRepository
    {
        public List<AudioPart> Items = [];
        public Dictionary<string, CombinedAudioSegment> Segments = [];
        public int Writes;
        public bool FailPublicationOnce;
        public Task<List<AudioPart>> Parts(string id) => Task.FromResult(Items);
        public Task<CombinedAudioSegment?> Find(string id) => Task.FromResult(Segments.GetValueOrDefault(id));
        public Task Save(CombinedAudioSegment item)
        {
            if (item.State == "published" && FailPublicationOnce) { FailPublicationOnce = false; throw new IOException("Simulated crash after file rename"); }
            Segments[item.Id] = JsonSerializer.Deserialize<CombinedAudioSegment>(JsonSerializer.Serialize(item))!; Writes++;
            return Task.CompletedTask;
        }
    }
}
