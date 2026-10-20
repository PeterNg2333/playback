using System.Text.Json;

// Run the real replay harness twice: conversion must affect the saved note, never its raw reply.
static class NoteReplayChecks
{
    public static async Task Run()
    {
        var variables = new[] { "PLAYBACK_NOTE_MODELS_SOURCE", "PLAYBACK_NOTE_MODELS_MINUTES", "PLAYBACK_NOTE_MODELS_SCRIPT", "PLAYBACK_NOTE_MODELS",
            "PLAYBACK_OFFLINE_TEST", "PLAYBACK_AUTO_NOTES", "PLAYBACK_NOTE_MODELS_MAX_USD", "PLAYBACK_NOTE_MODELS_RESUME" };
        var previous = variables.ToDictionary(x => x, Environment.GetEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable("PLAYBACK_NOTE_MODELS_SOURCE", "cantonese-fixture");
            Environment.SetEnvironmentVariable("PLAYBACK_NOTE_MODELS_MINUTES", "2");
            Environment.SetEnvironmentVariable("PLAYBACK_NOTE_MODELS", "fixture:literal-text");
            Environment.SetEnvironmentVariable("PLAYBACK_NOTE_MODELS_MAX_USD", null);
            Environment.SetEnvironmentVariable("PLAYBACK_NOTE_MODELS_RESUME", null);
            foreach (var script in new[] { "raw", "zh-Hant" })
            {
                Environment.SetEnvironmentVariable("PLAYBACK_NOTE_MODELS_SCRIPT", script);
                var folder = Path.Combine(Path.GetTempPath(), "playback-note-replay-" + Guid.NewGuid().ToString("N"));
                await NoteModelComparison.Run(folder, live: false);
                var output = Path.Combine(folder, "fixture_literal-text");
                var note = await File.ReadAllTextAsync(Path.Combine(output, "notes.md"));
                var replies = await File.ReadAllTextAsync(Path.Combine(output, "replies.json"));
                Expect.That(note.Contains(script == "raw" ? "示范" : "示範"), "Replay script did not control the saved note");
                using var raw = JsonDocument.Parse(replies);
                var rawBodies = raw.RootElement.EnumerateArray().SelectMany(x =>
                    JsonDocument.Parse(x.GetString()!).RootElement.GetProperty("blocks").EnumerateArray()
                        .Select(p => p.GetProperty("body").GetString()!)).ToList();
                Expect.That(rawBodies.Any(x => x.Contains("示范")), "Replay changed raw provider evidence");
                using var report = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "offline-results.json")));
                var input = report.RootElement.GetProperty("input");
                Expect.That(input.GetProperty("throughMs").GetInt64() == 120_000 && input.GetProperty("lectureLanguage").GetString()!.StartsWith("synthetic"),
                    "Replay lost the requested span or synthetic provenance");
            }
            var text = await File.ReadAllTextAsync(Week3Transcript.RelativePath);
            var full = NoteModelComparison.Span("full", text);
            Expect.That(full > 20 * 60_000 && Week3Transcript.Sources("full", text, full).Last().EndMs == full,
                "Full replay omitted the end of the repository lecture");
            Expect.Rejects(() => NoteModelComparison.Span("0", text), "Replay accepted zero minutes");
            Expect.Rejects(() => NoteModelComparison.Span("181", text), "Replay accepted more than three hours");
            Console.WriteLine("Replay checks passed: full lecture end, raw versus OpenCC saved notes, unchanged replies, synthetic provenance and bounded duration");
        }
        finally { foreach (var (key, value) in previous) Environment.SetEnvironmentVariable(key, value); }
    }
}
