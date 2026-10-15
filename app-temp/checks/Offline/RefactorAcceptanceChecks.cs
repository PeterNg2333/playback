using System.Diagnostics;
using System.Text.Json;
using Playback.Api.Activity;
using Playback.Api.Ask;
using Playback.Api.Notes;
using Playback.Api.Providers;
using Microsoft.Extensions.Logging.Abstractions;

// Three-hour virtual lecture: real application/domain paths, fixture providers/storage only.
static class RefactorAcceptanceChecks
{
    public static async Task Run()
    {
        Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "yes");
        var watch = Stopwatch.StartNew(); var clock = new TestClock(); var store = new MemoryStore {Clock = clock}; store.Add("three-hour");
        await store.SaveWorkspaceProcessingSettings(new(0, new(JevEnabled: false, OrganizationEnabled: false, CheckIntervalSeconds: 2, MaxPendingWaitSeconds: 10)));
        var writer = new FixtureNoteModel(); var activity = new AiActivity(store);
        await using var agent = new NoteAgent(store, writer, new FixtureNoteGate(), NullLogger<NoteAgent>.Instance, activity, clock);
        var origin = clock.GetUtcNow().UtcDateTime;
        for (var i = 0; i < 2700; i++)
        {
            store.Speech("three-hour", "lecture-" + i, $"TCP fact {i}: shared capacity applies only at one bottleneck.");
            var source = store.Transcripts["three-hour"][^1]; source.SourceId = i % 2 == 0 ? "microphone" : "system-audio";
            source.StartMs = i / 2 * 8000; source.EndMs = source.StartMs + 8000; source.RecordedAt = origin.AddMilliseconds(source.StartMs);
            if ((i + 1) % 60 == 0) {clock.Advance(240); await agent.Generate("three-hour", default);}
        }
        while (store.Transcripts["three-hour"].Any(x => x.NoteStatus != "completed")) await agent.Generate("three-hour", default);
        var saved = store.Notes["three-hour"];
        var adapter = new NoteStoreAdapter(store); var commands = new NoteCommands(adapter, adapter, adapter);
        Expect.That(saved.Coverage.Count(x => x.Status == "covered") == 2700 && store.Transcripts["three-hour"].All(x => x.NoteStatus == "completed"), "Long-session source coverage was lost or falsely left completed");
        Expect.That(store.Transcripts["three-hour"].Max(x => x.EndMs) == 10800000 && saved.Sections.Count <= 512 && writer.Calls < 100 && writer.InputLengths.All(x => x <= 48000), "Long-session admission or topic/input bounds were exceeded");
        var originalVersion = saved.Version; var originalBodies = saved.Sections.SelectMany(x => x.Blocks).ToDictionary(x => x.Id, x => x.Body);
        await commands.ApplyStructure("three-hour", originalVersion, [new() {Kind = "move-topic", TopicId = saved.Sections[^1].Id, ParentId = saved.Sections[0].Id}]);
        var moved = store.Notes["three-hour"];
        Expect.That(originalBodies.All(pair => moved.Sections.SelectMany(x => x.Blocks).Any(x => x.Id == pair.Key && x.Body == pair.Value)), "Long-session structural operation changed saved bodies");
        await using (var restarted = new NoteAgent(store, writer, new FixtureNoteGate(), NullLogger<NoteAgent>.Instance, activity, clock))
        {
            await store.RequestNoteFlush("three-hour"); clock.Advance(2); restarted.Tick(["three-hour"], default); await restarted.Drain();
            Expect.That(store.Notes["three-hour"].Version == originalVersion + 1 && !store.Gates["three-hour"].FlushRequested, "Restart/Stop duplicated completed evidence or lost flush acknowledgement");
        }
        var last = store.Transcripts["three-hour"][^1].Id; var answerModel = new AnswerModel(last);
        var answer = JsonSerializer.SerializeToElement(await new ChatAgent(store, answerModel, activity).Ask("three-hour", new("Explain TCP fact 2699 at the end"), default), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Expect.That(answer.GetProperty("lectureStatus").GetString() == "referenced" && answer.GetProperty("evidence").EnumerateArray().Any(x => x.GetProperty("id").GetString() == last), "Ask lost the late lecture source after structural organization/restart");
        var cursor = (string?)null; var pages = 0;
        do {var page = JsonSerializer.SerializeToElement(await store.SyncSession("three-hour", cursor)); cursor = page.GetProperty("cursor").GetString(); pages++; if (!page.GetProperty("hasMore").GetBoolean()) break;} while (pages < 100);
        var idle = JsonSerializer.SerializeToElement(await store.SyncSession("three-hour", cursor));
        Expect.That(pages < 100 && idle.GetProperty("changes").GetArrayLength() == 0, "Long-session sync failed its bounded drain/idle contract");
        await Expect.RejectsAsync(() => commands.EditBlock("three-hour", originalVersion, originalBodies.Keys.First(), "stale"), "Stale long-session edit overwrote a structural revision");
        Console.WriteLine($"Refactor acceptance passed offline: 3 virtual hours / 2 sources / 2,700 cited transcripts / {writer.Calls} fixture generations; structural retention, restart/Stop, late-source Ask, paged sync/idle and stale edits; {watch.ElapsedMilliseconds} ms (no DB, provider, capture or three-hour wall-clock claim)");
    }
    sealed class AnswerModel(string id) : GeminiLanguageModel
    {
        public override Task<string> Generate(string name, string instructions, string prompt, CancellationToken ct, string? model = null, Action<string>? onUpdate = null, Action<string>? onUsage = null) => Task.FromResult("A late lecture explanation [" + id + "]");
    }
}
