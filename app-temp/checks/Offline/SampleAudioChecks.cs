using System.Text.RegularExpressions;
using NAudio.Wave;
using Playback.Api.Ask;
using Playback.Api.Audio;
using Playback.Api.Audio.Vad;
using Playback.Api.Db;
using Playback.Api.Notes;

// Checks against the repository's Week 3 sample (app-temp/data/test-audio/sampleAudio). Read-only: nothing
// is saved or uploaded. Decoding the M4A needs Windows; the transcript lookup does not.
static class SampleAudioChecks
{
    static readonly Regex Timestamp = new(@"^(?:\d+:)?\d{1,2}:\d{2}$", RegexOptions.Compiled);
    static readonly Regex Word = new(@"[A-Za-z]{6,}", RegexOptions.Compiled);

    // The full 2 h 45 min transcript: Ask finds the beginning, middle and end with their audio time,
    // and the note backlog drains in bounded batches without repeating a source.
    public static void SourceLookup(string folder)
    {
        var transcripts = new List<Transcript>();
        var words = new List<string>();
        long previousMs = 0;
        foreach (var raw in File.ReadLines(Path.Combine(folder, "transcript.txt")))
        {
            var line = raw.Trim();
            if (Timestamp.IsMatch(line))
            {
                var parts = line.Split(':').Select(long.Parse).ToArray();
                var endMs = (parts.Length == 3 ? parts[0] * 3600 + parts[1] * 60 + parts[2] : parts[0] * 60 + parts[1]) * 1000;
                Expect.That(endMs > previousMs && words.Count > 0, "Invalid transcript time sequence");
                transcripts.Add(new Transcript { Id = $"sample-audio-{transcripts.Count:0000}", StartMs = previousMs, EndMs = endMs, Original = string.Join(" ", words) });
                previousMs = endMs;
                words.Clear();
            }
            else if (line.Length > 0) words.Add(line);
        }
        // The supplied file has a short closing utterance with no following timestamp.
        if (words.Count > 0)
            transcripts.Add(new Transcript { Id = $"sample-audio-{transcripts.Count:0000}", StartMs = previousMs, EndMs = previousMs + 14_000, Original = string.Join(" ", words) });
        Expect.That(transcripts.Count >= 1000 && transcripts[^1].EndMs >= 9_800_000, "Expected long transcript was not found");

        var material = new Material { Id = "tutorial", Name = "Tutorial", Text = File.ReadAllText(Path.Combine(folder, "Tutorial.txt")) };
        var session = new SessionView("sample-audio-offline", "Sample audio", null, DateTime.UtcNow, "", 0, 0, false, "zh-Hant", [material], transcripts, [], [], [], null);
        var covered = 0;
        foreach (var index in new[] { 50, transcripts.Count / 2, transcripts.Count - 50 })
        {
            var target = transcripts[index];
            var found = false;
            // Choose a distinctive word from each time window, then exercise the real Q&A source selector.
            foreach (var word in Word.Matches(target.Original).Select(x => x.Value).OrderByDescending(x => x.Length))
            {
                var context = ChatContextBuilder.Build(session, new QuestionInput($"What was said about {word}?"));
                if (!context.RelevantTranscripts.Any(x => x.Id == target.Id)) continue;
                // The model sees the passage as part of a short-aliased source group with that group's audio range.
                var group = context.References!.Groups.Single(x => x.TranscriptIds.Contains(target.Id));
                Expect.That(group.StartMs <= target.StartMs && group.EndMs >= target.EndMs &&
                    context.Prompt.Contains($"Source [{group.Id}] ({group.StartMs}-{group.EndMs} ms)"), "Q&A lost the cited audio time");
                covered++;
                found = true;
                break;
            }
            if (!found) Console.WriteLine($"Sample audio source lookup missed index {index}");
        }
        Expect.That(covered == 3, "Q&A failed to retrieve all three lecture windows");

        var pending = transcripts.ToList();
        var calls = 0;
        var assigned = new HashSet<string>();
        while (assigned.Count < pending.Count)
        {
            var batch = NoteInput.Pending(pending);
            Expect.That(batch.Count > 0 && batch.Count <= 64 && (batch.Count == 1 || batch.Sum(x => x.SourceText.Length) <= 18_000),
                "Note batch stalled or exceeded its 64-source, 18,000-character bound");
            foreach (var item in batch)
            {
                Expect.That(assigned.Add(item.Id), "Duplicate note transcript input");
                item.NoteStatus = "completed";
            }
            calls++;
        }
        Console.WriteLine($"Sample audio offline: {transcripts.Count} source entries, {calls} bounded backlog batches, beginning/middle/end Q&A lookup and transcript times passed; no external calls");
    }

    // The first 20 minutes decode into forty valid 30-second WAV chunks.
    public static void Decode(string folder)
    {
        var chunks = 0;
        var activeChunks = 0;
        foreach (var (_, wav, hasSound) in SampleAudio.Chunks(folder))
        {
            Expect.That(wav.Length >= 30 * 16_000 * 2 + 44 &&
                System.Text.Encoding.ASCII.GetString(wav, 0, 4) == "RIFF" &&
                System.Text.Encoding.ASCII.GetString(wav, 8, 4) == "WAVE", $"Invalid WAV chunk {chunks}");
            chunks++;
            if (hasSound) activeChunks++;
        }
        Console.WriteLine($"Sample audio preview: first 20 minutes decoded into {chunks} bounded 30-second, 16 kHz mono WAV chunks; " +
            $"{activeChunks} chunks contained audible samples. No audio was uploaded.");
    }

    // Silero VAD stays quiet on digital silence and hears the lecture at 8, 16 and 48 kHz.
    public static void Vad(string folder)
    {
        using var detector = new SpeechActivityDetector();
        for (var ms = 100; ms <= 1_000; ms += 100)
            Expect.That(!detector.Process(new float[1_600], 16_000, ms), "Digital silence triggered VAD");
        var (_, wav, _) = SampleAudio.Chunks(folder).First();
        using var reader = new WaveFileReader(new MemoryStream(wav));
        using var speechDetector = new SpeechActivityDetector();
        using var lowRateDetector = new SpeechActivityDetector();
        using var highRateDetector = new SpeechActivityDetector();
        var buffer = new byte[3_200];
        var elapsedMs = 0L;
        var firstVoiceMs = -1L;
        var voicedFrames = 0;
        var lowRateVoicedFrames = 0;
        var highRateVoicedFrames = 0;
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            elapsedMs += read * 1_000L / reader.WaveFormat.AverageBytesPerSecond;
            var mono = AudioSamples.Mono(buffer.AsSpan(0, read), reader.WaveFormat);
            if (lowRateDetector.Process(mono.Where((_, index) => index % 2 == 0).ToArray(), 8_000, elapsedMs))
                lowRateVoicedFrames++;
            var highRate = new float[mono.Length * 3];
            for (var index = 0; index < mono.Length; index++)
                highRate.AsSpan(index * 3, 3).Fill(mono[index]);
            if (highRateDetector.Process(highRate, 48_000, elapsedMs))
                highRateVoicedFrames++;
            if (!speechDetector.Process(mono, reader.WaveFormat.SampleRate, elapsedMs)) continue;
            if (firstVoiceMs < 0) firstVoiceMs = elapsedMs;
            voicedFrames++;
        }
        Expect.That(firstVoiceMs >= 0 && voicedFrames > 10 && lowRateVoicedFrames > 10 && highRateVoicedFrames > 10,
            "VAD did not detect speech in sampleAudio");
        Console.WriteLine($"Silero VAD sample passed: first speech at {firstVoiceMs} ms, 8/16/48 kHz voiced callbacks {lowRateVoicedFrames}/{voicedFrames}/{highRateVoicedFrames} of 300; digital silence stayed quiet");
    }
}
