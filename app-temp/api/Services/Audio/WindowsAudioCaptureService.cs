using Playback.Api.Db;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using NAudio.Wave;

namespace Playback.Api.Services.Audio;

public sealed record ActiveCaptureSegment(string SourceId, long StartMs, long EndMs, DateTime RecordedAt, bool Streaming,
    string? InterimText = null, string? RawInterimText = null, long? LatencyMs = null, string? Model = null);

public sealed record CaptureStatus(string State, string? SessionId, Dictionary<string, long> Bytes, bool AutoAsr, bool NoSoundWarning, string? Error,
    Dictionary<string, int> Levels, long CapturedThroughMs, long LastFinalizedAtMs, ActiveCaptureSegment[] ActiveSegments,
    string? SourceMode = null, int ChunkMilliseconds = LiveAsrSession.ChunkMilliseconds, string? InterimError = null,
    long RecordingElapsedMs = 0, string? RecordingId = null);

public sealed class WindowsAudioCaptureService(PlaybackStore store, AsrQueue asr, ILogger<WindowsAudioCaptureService> logger, IAsrAdapter adapter) : IAsyncDisposable
{
    readonly SemaphoreSlim transition = new(1, 1);
    readonly string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "local-capture"));
    Recording? active;
    Recording? recent;
    string? lastError;

    public CaptureStatus Status()
    {
        var recording = active;
        var visible = recording ?? recent;
        CaptureSource[] sources;
        if (recording is null) sources = [];
        else lock (recording.Sources) sources = recording.Sources.ToArray();
        CaptureSource[] retained;
        if (visible is null) retained = [];
        else lock (visible.Sources) retained = visible.Sources.ToArray();
        var capturedThroughMs = recording is null ? 0 : recording.OffsetMs + recording.Clock.ElapsedMilliseconds;
        return new(recording is null ? "idle" : recording.Paused ? "paused" : "recording", visible?.SessionId,
            sources.ToDictionary(source => source.Name, source => source.Bytes),
            recording?.AutoAsr ?? false,
            recording is { Paused: false } && DateTime.UtcNow - recording.LastSoundAt > TimeSpan.FromMinutes(1),
            recording?.Error ?? lastError,
            sources.ToDictionary(source => source.Name, source => source.Level),
            capturedThroughMs,
            recording?.LastFinalizeAtMs ?? 0,
            retained.SelectMany(source => source.Live?.Segments ?? [])
                .Concat(visible?.RetainedSegments ?? [])
                .Concat(recording is { Paused: false } ? sources.Select(source => source.ActiveSegment(capturedThroughMs)).OfType<ActiveCaptureSegment>() : [])
                .GroupBy(segment => (segment.SourceId, segment.StartMs))
                .Select(group => (group.FirstOrDefault(segment => segment.InterimText is not null) ?? group.Last())
                    with { Streaming = group.Any(segment => segment.Streaming) }).ToArray(),
            recording?.SourceMode, LiveAsrSession.ChunkMilliseconds,
            retained.Select(source => source.Live?.Error).FirstOrDefault(error => error is not null),
            visible?.Clock.ElapsedMilliseconds ?? 0, visible?.Id);
    }

    public async Task<CaptureStatus> Start(string sessionId, string sourceMode = "both")
    {
        if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Local capture currently requires Windows");
        if (!Regex.IsMatch(sessionId, "^[a-f0-9]{32}$")) throw new InvalidOperationException("Invalid session ID");
        _ = CaptureSourceModes.Sources(sourceMode);
        await transition.WaitAsync();
        try
        {
            if (active is not null) throw new InvalidOperationException("Recording is already active");
            lastError = null;
            await ReplayPending(sessionId);
            var session = await store.Session(sessionId) ?? throw new InvalidOperationException("Session not found");
            var offset = session.Chunks.Select(chunk => chunk.EndMs)
                .Concat(Pending(sessionId).Select(chunk => chunk.EndMs)).DefaultIfEmpty().Max();
            var recording = new Recording(sessionId, offset, sourceMode);

            try
            {
                await StartSources(recording);
            }
            catch
            {
                await CloseSources(recording);
                recording.Cancel.Dispose();
                throw;
            }
            recent = null;
            active = recording;
            recording.Pump = Pump(recording);
            return Status();
        }
        finally { transition.Release(); }
    }

    [SupportedOSPlatform("windows")]
    async Task StartSources(Recording recording)
    {
        var failures = new List<string>();
        foreach (var name in CaptureSourceModes.Sources(recording.SourceMode))
        {
            var builder = name == "system"
                ? new WasapiRecorderBuilder().WithLoopbackCapture()
                : new WasapiRecorderBuilder();
            if (await TryStartSource(recording, name, builder) is { } error)
                failures.Add($"{(name == "system" ? "System audio" : "Microphone")}: {error}");
        }
        int sourceCount;
        lock (recording.Sources) sourceCount = recording.Sources.Count;
        if (sourceCount == 0)
            throw new InvalidOperationException(
                $"No audio source could start. {string.Join("; ", failures)}");
        recording.Error = failures.Count > 0 ? string.Join("; ", failures) : null;
    }

    [SupportedOSPlatform("windows")]
    async Task<string?> TryStartSource(Recording recording, string name, WasapiRecorderBuilder builder)
    {
        WasapiRecorder? recorder = null;
        CaptureSource? source = null;
        LiveAsrSession? live = null;
        try
        {
            recorder = builder.Build();
            var sourceStartMs = recording.OffsetMs + recording.Clock.ElapsedMilliseconds;
            if (recording.AutoAsr) live = new LiveAsrSession(adapter, recording.SessionId, name,
                sourceStartMs, () => store.SessionSettings(recording.SessionId));
            source = new CaptureSource(name, recorder, Path.Combine(root, recording.SessionId, name), recording, live, sourceStartMs);
            recorder.StartRecording();
            lock (recording.Sources) recording.Sources.Add(source);
            return null;
        }
        catch (Exception ex)
        {
            if (recorder is not null)
            {
                try { await recorder.DisposeAsync(); }
                catch (Exception closeError) { logger.LogWarning(closeError, "Capture device cleanup failed"); }
            }
            source?.Detector.Dispose();
            if (live is not null) await live.DisposeAsync();
            logger.LogWarning(ex, "{Source} audio capture unavailable", name);
            return ex.Message;
        }
    }

    async Task Pump(Recording recording)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            while (await timer.WaitForNextTickAsync(recording.Cancel.Token))
            {
                if (recording.Paused) continue;
                var end = recording.OffsetMs + recording.Clock.ElapsedMilliseconds;
                CaptureSource[] sources;
                lock (recording.Sources) sources = recording.Sources.ToArray();
                var elapsed = end - recording.LastFinalizeAtMs;
                if (elapsed >= LiveAsrSession.ChunkMilliseconds || elapsed >= 3_000 &&
                    sources.Any(source => source.HasSpeech) && sources.All(source => source.Quiet(end)))
                    await FinalizeChunks(recording);
                else if (!asr.LiveBacklog)
                    foreach (var source in sources) source.Preview(end);
            }
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
            recording.LastFinalizeAtMs = endMs;
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
            source.Detector.Dispose();
            if (source.Live is not null) await source.Live.DisposeAsync();
            if (source.Live is not null)
                recording.RetainedSegments = recording.RetainedSegments.Concat(source.Live.Segments).TakeLast(48).ToArray();
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
            recording.Clock.Stop();
            await FinalizeChunks(recording);
            lastError = recording.Error;
            recent = recording;
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
            lock (active.Sources) active.Sources.Clear();
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
                await StartSources(active);
            }
            catch
            {
                await CloseSources(active);
                lock (active.Sources) active.Sources.Clear();
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

    sealed class Recording(string sessionId, long offsetMs, string sourceMode)
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string SessionId { get; } = sessionId;
        public string SourceMode { get; } = sourceMode;
        public long OffsetMs { get; } = offsetMs;
        public long LastFinalizeAtMs { get; set; } = offsetMs;
        public bool AutoAsr => Environment.GetEnvironmentVariable("PLAYBACK_PAUSE_EXTERNAL_ASR") != "yes"
            && Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST") != "yes";
        public Stopwatch Clock { get; } = Stopwatch.StartNew();
        public bool Paused { get; set; }
        public DateTime LastSoundAt { get; set; } = DateTime.UtcNow;
        public CancellationTokenSource Cancel { get; } = new();
        public List<CaptureSource> Sources { get; } = [];
        public ActiveCaptureSegment[] RetainedSegments { get; set; } = [];
        public Task Pump { get; set; } = Task.CompletedTask;
        public SemaphoreSlim FinalizeGate { get; } = new(1, 1);
        public string? Error { get; set; }
    }

    sealed class CaptureSource
    {
        readonly object gate = new();
        readonly string directory;
        readonly Recording recording;
        readonly SpeechActivityDetector detector = new();
        WaveFileWriter? writer;
        string? partPath;
        long sequence;
        long startMs;
        long bytes;
        int level;
        bool hasSound;

        public string Name { get; }
        public WasapiRecorder Recorder { get; }
        public SpeechActivityDetector Detector => detector;
        public bool HasSpeech { get { lock (gate) return hasSound; } }
        public bool Quiet(long endMs) { lock (gate) return detector.QuietForMs(endMs) >= 700; }
        public LiveAsrSession? Live { get; }
        public void Preview(long endMs) { lock (gate) Live?.Tick(endMs, hasSound); }
        public long Bytes => Interlocked.Read(ref bytes);
        public int Level => Volatile.Read(ref level);

        public ActiveCaptureSegment? ActiveSegment(long endMs)
        {
            lock (gate)
                return hasSound && endMs > startMs
                    ? new(Name, startMs, endMs, DateTime.UtcNow.AddMilliseconds(startMs - endMs),
                        detector.VoicedMs >= 1_000 && detector.Speaking(endMs))
                    : null;
        }

        public CaptureSource(string name, WasapiRecorder recorder, string directory, Recording recording, LiveAsrSession? live, long sourceStartMs)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            Name = name;
            Live = live;
            Recorder = recorder;
            this.directory = directory;
            this.recording = recording;
            startMs = sourceStartMs;
            var format = recorder.WaveFormat;
            recorder.DataAvailable += (buffer, _, _, _) =>
            {
                Volatile.Write(ref level, AudioActivity.Level(buffer, format));
                var soundDetected = false;
                try
                {
                    var mono = AudioActivity.Mono(buffer, format);
                    lock (gate)
                        soundDetected = detector.Process(mono, format.SampleRate,
                            recording.OffsetMs + recording.Clock.ElapsedMilliseconds);
                    Live?.Feed(mono, format.SampleRate, soundDetected);
                }
                catch (Exception ex) { recording.Error = $"{name} VAD stopped: {ex.Message}"; }
                if (soundDetected)
                    recording.LastSoundAt = DateTime.UtcNow;
                lock (gate)
                {
                    try
                    {
                        if (soundDetected) hasSound = true;
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
                hasSound = false;
                Live?.Rotate(endMs);
            }
        }
    }
}
