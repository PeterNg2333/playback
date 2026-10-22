using NAudio.Wave;
using Playback.Application.Recording;
using Playback.Application.Workspace;
using Playback.Infrastructure.Audio;
using Playback.Infrastructure.Audio.Recording;

// Recording: only the chosen audio sources open, and the level meter reads microphone and system samples.
static class RecordingChecks
{
    public static void Run()
    {
        Expect.That(CaptureSourceModes.Sources("microphone").SequenceEqual(["microphone"]) &&
            CaptureSourceModes.Sources("system").SequenceEqual(["system"]) &&
            CaptureSourceModes.Sources("both").SequenceEqual(["microphone", "system"]),
            "Recording must open only the selected audio sources");
        Expect.Rejects(() => CaptureSourceModes.Sources("unknown"), "Unknown recording modes must fail before starting capture");

        var floatFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        var quiet = new byte[480 * 4];
        var voice = new byte[480 * 4];
        for (var i = 0; i < 480; i++) BitConverter.GetBytes(0.08f).CopyTo(voice, i * 4);
        Expect.That(AudioSamples.Level(quiet, floatFormat) == 0 && AudioSamples.Level(voice, floatFormat) > 0,
            "Live waveform level must reflect captured microphone samples");
        var stereoFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
        var rightOnly = new byte[480 * 2 * 4];
        for (var i = 0; i < 480; i++) BitConverter.GetBytes(0.08f).CopyTo(rightOnly, i * 8 + 4);
        Expect.That(AudioSamples.Level(rightOnly, stereoFormat) > 0, "System audio on the right channel must show on the level meter");

        Console.WriteLine("Recording checks passed: selected sources only, level meter for microphone and right-channel system audio");
    }

    // Browser recording: the stream is the owner's, stays closed once closed, and only Stop asks for the after-lecture notes.
    public static async Task UploadedStreams()
    {
        var session = new string('a', 32);
        var stream = new string('b', 32);
        var access = new FakeAccess();
        var streams = new FakeStreams();
        var commands = new UploadedRecordingCommands(streams, access);

        await commands.Update(session, stream, closed: false, stop: false);
        await commands.Update(session, stream, closed: false, stop: false);
        Expect.That(streams.Saved.Count == 2 && streams.Saved.All(x => x == (session, stream, false)) && access.Owned.All(x => x == session),
            "Opening and renewing a browser stream must check the session owner and keep the stream open");
        Expect.That(access.Flushes.Count == 0, "Renewing a stream must not ask for the after-lecture notes");

        await commands.Update(session, stream, closed: true, stop: false);
        Expect.That(streams.Saved[^1] == (session, stream, true) && access.Flushes.Count == 0, "Pause must close the stream without finishing notes");
        await commands.Update(session, stream, closed: false, stop: false);
        Expect.That(streams.Saved.Count == 3, "A late renewal must not reopen a closed stream");

        await commands.Update(session, stream, closed: true, stop: true);
        Expect.That(access.Flushes.SequenceEqual([session]), "Stop must ask for the after-lecture notes once");

        await Expect.RejectsAsync(() => commands.Update(session, "not-a-stream", false, false), "Stream IDs must be 32 hex characters");
        await Expect.RejectsAsync(() => commands.Update(session, new string('c', 32), closed: false, stop: true), "Stop without closing must be rejected");
        var foreign = false;
        try { await commands.Update(new string('d', 32), stream, closed: true, stop: true); }
        catch (WorkspaceNotFoundException) { foreign = true; }
        Expect.That(foreign && access.Flushes.Count == 1, "Another session must not close or finish a stream it does not own");

        Console.WriteLine("Browser recording stream checks passed: owner, renew, pause, stop, closed stays closed, foreign stream");
    }

    sealed class FakeAccess : IRecordingAccess
    {
        public List<string> Owned { get; } = [];
        public List<string> Flushes { get; } = [];
        public Task RequireSessionOwner(string id) { Owned.Add(id); return Task.CompletedTask; }
        public Task RequestNoteFlush(string id) { Flushes.Add(id); return Task.CompletedTask; }
    }

    sealed class FakeStreams : IUploadedStreams
    {
        public List<(string Session, string Stream, bool Closed)> Saved { get; } = [];
        public Task<UploadedStreamState?> Find(string streamId) =>
            Task.FromResult(Saved.LastOrDefault(x => x.Stream == streamId) is { Session: not null } last ? new UploadedStreamState(last.Session, last.Closed) : null);
        public Task Save(string sessionId, string streamId, bool closed) { Saved.Add((sessionId, streamId, closed)); return Task.CompletedTask; }
    }
}
