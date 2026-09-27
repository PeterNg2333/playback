using System.Text.RegularExpressions;
using Playback.Api.Db;
using Playback.Api.Endpoints;
using Playback.Api.Services.Ai.Agents;

internal static class SampleAudioOfflineCheck
{
    static readonly Regex Timestamp = new(@"^(?:\d+:)?\d{1,2}:\d{2}$", RegexOptions.Compiled);
    static readonly Regex Word = new(@"[A-Za-z]{6,}", RegexOptions.Compiled);

    public static void Run(string folder)
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
                var endMs = (parts.Length == 3
                    ? parts[0] * 3600 + parts[1] * 60 + parts[2]
                    : parts[0] * 60 + parts[1]) * 1000;
                if (endMs <= previousMs || words.Count == 0) throw new InvalidOperationException("Invalid transcript time sequence");
                transcripts.Add(new Transcript
                {
                    Id = $"sample-audio-{transcripts.Count:0000}", StartMs = previousMs,
                    EndMs = endMs, Original = string.Join(" ", words)
                });
                previousMs = endMs;
                words.Clear();
            }
            else if (line.Length > 0) words.Add(line);
        }
        // The supplied file has a short closing utterance with no following timestamp.
        if (words.Count > 0)
            transcripts.Add(new Transcript
            {
                Id = $"sample-audio-{transcripts.Count:0000}", StartMs = previousMs,
                EndMs = previousMs + 14_000, Original = string.Join(" ", words)
            });
        if (transcripts.Count < 1000 || transcripts[^1].EndMs < 9_800_000)
            throw new InvalidOperationException("Expected long transcript was not found");

        var material = new Material { Id = "tutorial", Name = "Tutorial", Text = File.ReadAllText(Path.Combine(folder, "Tutorial.txt")) };
        var session = new SessionView("sample-audio-offline", "Sample audio", null, DateTime.UtcNow,
            "", 0, 0, false, "zh-Hant", [material], transcripts, [], [], [], null);
        var covered = 0;
        foreach (var index in new[] { 50, transcripts.Count / 2, transcripts.Count - 50 })
        {
            var found = false;
            // Choose a distinctive word from each time window, then exercise the real Q&A source selector.
            foreach (var word in Word.Matches(transcripts[index].Original)
                .Select(x => x.Value).OrderByDescending(x => x.Length))
            {
                var context = ChatContextBuilder.Build(session, new QuestionInput($"What was said about {word}?"));
                if (!context.RelevantTranscripts.Any(x => x.Id == transcripts[index].Id)) continue;
                if (!context.Prompt.Contains($"Source [{transcripts[index].Id}] ({transcripts[index].StartMs}-{transcripts[index].EndMs} ms)"))
                    throw new InvalidOperationException("Q&A lost the cited audio time");
                covered++;
                found = true;
                break;
            }
            if (!found) Console.WriteLine($"Sample audio source lookup missed index {index}");
        }
        if (covered != 3) throw new InvalidOperationException("Q&A failed to retrieve all three lecture windows");

        var pending = transcripts.Select(x => x).ToList();
        var calls = 0;
        var assigned = new HashSet<string>();
        while (assigned.Count < pending.Count)
        {
            var batch = NoteAgent.Pending(pending);
            if (batch.Count == 0 || batch.Count > 40) throw new InvalidOperationException("Note batch stalled or exceeded limit");
            foreach (var item in batch)
            {
                if (!assigned.Add(item.Id)) throw new InvalidOperationException("Duplicate note transcript input");
                item.NoteStatus = "completed";
            }
            calls++;
        }
        Console.WriteLine($"Sample audio offline: {transcripts.Count} source entries, {calls} bounded backlog batches, beginning/middle/end Q&A lookup and transcript times passed; no external calls");
    }
}
