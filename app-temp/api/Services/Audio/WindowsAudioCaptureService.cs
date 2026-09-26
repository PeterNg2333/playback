using Playback.Api.Db;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using NAudio.Wave;

namespace Playback.Api.Services.Audio;

public sealed record CaptureStatus(string State, string? SessionId, Dictionary<string, long> Bytes, bool AutoAsr, bool NoSoundWarning, string? Error);

public sealed class WindowsAudioCaptureService(PlaybackStore store, AsrQueue asr, ILogger<WindowsAudioCaptureService> logger) : IAsyncDisposable
{
    readonly SemaphoreSlim transition = new(1, 1);
    readonly string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "local-capture"));
    Recording? active;
    string? lastError;

    public CaptureStatus Status()
    {
        var recording = active;
        return new(recording is null ? "idle" : recording.Paused ? "paused" : "recording", recording?.SessionId,
            recording?.Sources.ToDictionary(source => source.Name, source => source.Bytes) ?? [],
            recording?.AutoAsr ?? false,
            recording is { Paused: false } && DateTime.UtcNow - recording.LastSoundAt > TimeSpan.FromMinutes(1),
            recording?.Error ?? lastError);
    }

    public async Task<CaptureStatus> Start(string sessionId)
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Local capture currently requires Windows");
        if (!Regex.IsMatch(sessionId, "^[a-f0-9]{32}$")) throw new InvalidOperationException("Invalid session ID");
        await transition.WaitAsync();
        try
        {
            if (active is not null) throw new InvalidOperationException("Recording is already active");
            lastError = null;
            await ReplayPending(sessionId);
            var session = await store.Session(sessionId) ?? throw new InvalidOperationException("Session not found");
            var offset = session.Chunks.Select(chunk => chunk.EndMs)
                .Concat(Pending(sessionId).Select(chunk => chunk.EndMs)).DefaultIfEmpty().Max();
            var recording = new Recording(sessionId, offset);

            // A working microphone is the minimum viable source; loopback can fail independently.
            try
            {
                await StartSource(recording, "microphone", new WasapiRecorderBuilder(), required: true);
                await StartSource(recording, "system", new WasapiRecorderBuilder().WithLoopbackCapture(), required: false);
            }
            catch
            {
                await CloseSources(recording);
                recording.Cancel.Dispose();
                throw;
            }
            active = recording;
            recording.Pump = Pump(recording);
            return Status();
        }
        finally { transition.Release(); }
    }

    [SupportedOSPlatform("windows")]
    async Task StartSource(Recording recording, string name, WasapiRecorderBuilder builder, bool required)
    {
        WasapiRecorder? recorder = null;
        try
        {
            recorder = builder.Build();
            var source = new CaptureSource(name, recorder, Path.Combine(root, recording.SessionId, name), recording);
            recorder.StartRecording();
            recording.Sources.Add(source);
        }
        catch (Exception ex)
        {
            if (recorder is not null)
            {
                try { await recorder.DisposeAsync(); }
                catch (Exception closeError) { logger.LogWarning(closeError, "Capture device cleanup failed"); }
            }
            if (required) throw new InvalidOperationException($"Microphone could not start: {ex.Message}", ex);
            recording.Error = $"System audio unavailable; microphone is recording. {ex.Message}";
            logger.LogWarning(ex, "System audio capture unavailable");
        }
    }

    async Task Pump(Recording recording)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(recording.Cancel.Token))
                if (!recording.Paused) await FinalizeChunks(recording);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            recording.Error = $"Capture writer stopped: {ex.Message}";
            logger.LogError(ex, "Capture writer stopped");
        }
    }

    async Task FinalizeChunks(Recording recording)
    {
        await recording.FinalizeGate.WaitAsync();
        try
        {
            var endMs = recording.OffsetMs + recording.Clock.ElapsedMilliseconds;
            foreach (var source in recording.Sources)
            {
                try { source.Rotate(endMs); }
                catch (Exception ex)
                {
                    recording.Error = $"{source.Name} WAV could not be finalized: {ex.Message}";
                    logger.LogError(ex, "Capture WAV could not be finalized");
                }
            }
            await ReplayPending(recording.SessionId, recording);
        }
        finally { recording.FinalizeGate.Release(); }
    }

    [SupportedOSPlatform("windows")]
    static async Task CloseSources(Recording recording)
    {
        foreach (var source in recording.Sources)
        {
            try { source.Recorder.StopRecording(); }
            catch (Exception ex) { recording.Error = $"{source.Name} stop: {ex.Message}"; }
        }
        foreach (var source in recording.Sources)
        {
            try { await source.Recorder.DisposeAsync(); }
            catch (Exception ex) { recording.Error = $"{source.Name} close: {ex.Message}"; }
        }
    }

    public async Task<CaptureStatus> Stop()
    {
        await transition.WaitAsync();
        try
        {
            if (active is null) return Status();
            if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Local capture requires Windows");
            var recording = active;
            recording.Cancel.Cancel();
            await recording.Pump;
            await CloseSources(recording);
            await FinalizeChunks(recording);
            lastError = recording.Error;
            active = null;
            recording.Cancel.Dispose();
            return Status();
        }
        finally { transition.Release(); }
    }

    public async Task<CaptureStatus> Pause()
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Local capture requires Windows");
        await transition.WaitAsync();
        try
        {
            if (active is null || active.Paused) return Status();
            active.Paused = true;
            active.Clock.Stop();
            await CloseSources(active);
            await FinalizeChunks(active);
            active.Sources.Clear();
            return Status();
        }
        finally { transition.Release(); }
    }

    public async Task<CaptureStatus> Resume()
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Local capture requires Windows");
        await transition.WaitAsync();
        try
        {
            if (active is null || !active.Paused) return Status();
            active.Clock.Start();
            active.LastSoundAt = DateTime.UtcNow;
            try
            {
                await StartSource(active, "microphone", new WasapiRecorderBuilder(), required: true);
                await StartSource(active, "system", new WasapiRecorderBuilder().WithLoopbackCapture(), required: false);
            }
            catch
            {
                await CloseSources(active);
                active.Sources.Clear();
                active.Clock.Stop();
                throw;
            }
            active.Paused = false;
            return Status();
        }
        finally { transition.Release(); }
    }

    IEnumerable<PendingChunk> Pending(string sessionId)
    {
        foreach (var source in new[] { "microphone", "system" })
        {
            var directory = Path.Combine(root, sessionId, source);
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.wav"))
            {
                var parts = Path.GetFileNameWithoutExtension(file).Split('-');
                if (parts.Length == 3 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)
                    && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var start)
                    && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var end) && end > start)
                    yield return new(sessionId, source, sequence, start, end, file);
            }
        }
    }

    async Task ReplayPending(string sessionId, Recording? recording = null)
    {
        foreach (var chunk in Pending(sessionId))
        {
            try
            {
                var saved = await store.SaveLocalChunk(chunk.SessionId, chunk.SourceId, chunk.Sequence,
                    chunk.StartMs, chunk.EndMs, chunk.Path, CancellationToken.None);
                asr.Enqueue(saved.Id);
                File.Delete(chunk.Path);
            }
            catch (Exception ex)
            {
                var message = $"Saved {chunk.SourceId} WAV locally but could not add it to the session: {ex.Message}";
                if (recording is not null) recording.Error = message;
                else lastError = message;
                logger.LogError(ex, "Local WAV remains pending at {Path}", chunk.Path);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Stop();
        transition.Dispose();
    }

    sealed record PendingChunk(string SessionId, string SourceId, long Sequence, long StartMs, long EndMs, string Path);

    sealed class Recording(string sessionId, long offsetMs)
    {
        public string SessionId { get; } = sessionId;
        public long OffsetMs { get; } = offsetMs;
        public bool AutoAsr => true;
        public Stopwatch Clock { get; } = Stopwatch.StartNew();
        public bool Paused { get; set; }
        public DateTime LastSoundAt { get; set; } = DateTime.UtcNow;
        public CancellationTokenSource Cancel { get; } = new();
        public List<CaptureSource> Sources { get; } = [];
        public Task Pump { get; set; } = Task.CompletedTask;
        public SemaphoreSlim FinalizeGate { get; } = new(1, 1);
        public string? Error { get; set; }
    }

    sealed class CaptureSource
    {
        readonly object gate = new();
        readonly string directory;
        readonly Recording recording;
        WaveFileWriter? writer;
        string? partPath;
        long sequence;
        long startMs;
        long bytes;

        public string Name { get; }
        public WasapiRecorder Recorder { get; }
        public long Bytes => Interlocked.Read(ref bytes);

        public CaptureSource(string name, WasapiRecorder recorder, string directory, Recording recording)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            Name = name;
            Recorder = recorder;
            this.directory = directory;
            this.recording = recording;
            startMs = recording.OffsetMs + recording.Clock.ElapsedMilliseconds;
            var format = recorder.WaveFormat;
            recorder.DataAvailable += (buffer, _, _, _) =>
            {
                if (name == "microphone" && AudioActivity.HasSound(buffer, format))
                    recording.LastSoundAt = DateTime.UtcNow;
                lock (gate)
                {
                    try
                    {
                        if (writer is null)
                        {
                            Directory.CreateDirectory(directory);
                            sequence = DateTime.UtcNow.Ticks;
                            partPath = Path.Combine(directory, FormattableString.Invariant($"{sequence}-{startMs}.wav.part"));
                            writer = new WaveFileWriter(partPath, format);
                        }
                        writer.Write(buffer);
                        Interlocked.Add(ref bytes, buffer.Length);
                    }
                    catch (Exception ex) { recording.Error = $"{name} audio could not be saved: {ex.Message}"; }
                }
            };
            recorder.RecordingStopped += (_, _) =>
            {
                if (!recording.Cancel.IsCancellationRequested && !recording.Paused)
                    recording.Error = $"{name} capture stopped unexpectedly; check the audio device";
            };
        }

        public void Rotate(long endMs)
        {
            lock (gate)
            {
                if (endMs <= startMs) return;
                if (writer is not null)
                {
                    writer.Dispose();
                    writer = null;
                    File.Move(partPath!, Path.Combine(directory, FormattableString.Invariant($"{sequence}-{startMs}-{endMs}.wav")));
                    partPath = null;
                }
                startMs = endMs;
            }
        }
    }
}
