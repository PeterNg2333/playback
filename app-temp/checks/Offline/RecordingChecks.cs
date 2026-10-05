using NAudio.Wave;
using Playback.Api.Audio;
using Playback.Api.Audio.Recording;

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
}
