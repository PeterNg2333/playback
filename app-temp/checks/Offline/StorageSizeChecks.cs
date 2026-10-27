using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Playback.Domain;
using Playback.Domain.Models;
using Playback.Infrastructure.Storage;

// Saved documents leave out null fields and AI activity keeps instructions once in the data folder, without changing
// what a read gives back, including documents saved before either change.
static class StorageSizeChecks
{
    public static async Task Run()
    {
        DocumentMappings.Register();
        var transcript = new Transcript { Id = "t1", SessionId = "s1", Original = "原文", ReviewStatus = "pending" };
        var stored = transcript.ToBsonDocument();
        Expect.That(!stored.Contains("ReviewError") && !stored.Contains("SupersededBy") && !stored.Contains("Translation") &&
            !stored.Contains("DisplayOriginal") && stored["Original"] == "原文", "Null transcript fields are still written");
        var back = BsonSerializer.Deserialize<Transcript>(stored);
        Expect.That(back.SupersededBy is null && back.Active && back.Translation is null && back.SourceText == "原文",
            "A transcript without null fields does not read back as before");
        var legacy = stored.DeepClone().AsBsonDocument.Add("SupersededBy", BsonNull.Value).Add("ReviewError", BsonNull.Value);
        Expect.That(BsonSerializer.Deserialize<Transcript>(legacy) is { Active: true, ReviewError: null }, "A document saved with explicit nulls no longer reads");

        // Note versions are snapshots; their line edits are worked out from the version before, the same as the save made them.
        Note Version(Note? previous, params string[] bodies)
        {
            var sections = new List<NoteSection> { new() { Id = "topic-1", Title = "Fourier", Blocks = bodies.Select((body, i) =>
                new NoteBlock { Id = "b" + i, Order = i, Body = body }).ToList() } };
            var update = NoteMutation.Prepare("s1", new SectionUpdate(sections, [], null, null));
            return NoteMutation.Create("s1", previous, update, "input-" + bodies.Length, previous?.Version ?? 0, "zh-Hant", "zh-Hant", "agent",
                new DateTime(2026, 10, 27, 1, 0, 0, DateTimeKind.Utc));
        }
        var first = Version(null, "訊號可以拆成頻率。");
        var second = Version(first, "訊號可以拆成頻率。", "FFT 將計算由 N² 減到 N log N。");
        Expect.That(second.Edits.Count > 0 && !second.ToBsonDocument().Contains("Edits"), "Note edits are still stored with the version");
        string Json(object value) => System.Text.Json.JsonSerializer.Serialize(value);
        Expect.That(Json(NoteChangeLog.Between(first, second)) == Json(second.Edits) && Json(NoteChangeLog.Between(null, first)) == Json(first.Edits),
            "Edits worked out from the previous version differ from the ones the save made");

        var instructions = "Write lecture notes in Traditional Chinese.";
        var activity = new ActivityRecord { Id = "a1", SessionId = "s1", Task = "Notes", PromptText = instructions, PromptHash = ContentHash.Of(instructions) };
        var filed = PlaybackStore.ActivityDocument(activity, promptFiled: true);
        Expect.That(!filed.Contains("PromptText") && filed["PromptHash"] == activity.PromptHash && filed["_id"] == "a1" &&
            !filed.Contains("Summary") && !filed.Contains("EndedAt"), "An activity with its prompt filed still stores the text or null fields");
        Expect.That(PlaybackStore.ActivityDocument(activity, promptFiled: false)["PromptText"] == instructions,
            "An activity whose prompt could not be filed lost its text");

        var folder = Path.Combine(Path.GetTempPath(), "playback-prompts-" + Guid.NewGuid().ToString("N"));
        try
        {
            var files = new PromptFiles(folder);
            Expect.That(await files.Save(activity.PromptHash!, instructions) && await files.Save(activity.PromptHash!, instructions),
                "The same prompt could not be saved twice");
            Expect.That(Directory.GetFiles(folder) is [var only] && Path.GetFileName(only) == activity.PromptHash + ".txt" &&
                await files.Read(activity.PromptHash!) == instructions, "A repeated prompt is not one file that reads back");
            Expect.That(!await files.Save(ContentHash.Of("other"), instructions) && !await files.Save("../escape", instructions) &&
                await files.Read("../escape") is null && await files.Read(ContentHash.Of("missing")) is null,
                "Prompt files accept a hash that does not name their text");

            // Once saved or read, a prompt comes from memory: the mount is not touched again.
            File.Delete(Path.Combine(folder, activity.PromptHash + ".txt"));
            Expect.That(await files.Read(activity.PromptHash!) == instructions && await files.Save(activity.PromptHash!, instructions) &&
                Directory.GetFiles(folder).Length == 0, "A known prompt is read or saved through the mount again");
            var restarted = new PromptFiles(folder);
            var other = "Translate into English.";
            await File.WriteAllTextAsync(Path.Combine(folder, ContentHash.Of(other) + ".txt"), other);
            Expect.That(await restarted.Read(ContentHash.Of(other)) == other, "A prompt filed before a restart does not read back");
            var corrupt = ContentHash.Of("whole text");
            await File.WriteAllTextAsync(Path.Combine(folder, corrupt + ".txt"), "whole te");
            Expect.That(await restarted.Read(corrupt) is null, "A truncated prompt file reads as its text");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        Console.WriteLine("Storage size checks passed: null fields left out and read back, explicit-null documents still read, " +
            "note edits not stored and worked out from the previous version exactly as saved, " +
            "activity prompts kept once by hash in the data folder with the text kept in the document when filing fails, " +
            "known prompts served from memory, files from before a restart read and truncated ones refused (temporary folder only)");
    }
}
