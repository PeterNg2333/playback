using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Playback.Api.Activity;
using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Api.Providers;
using Playback.Application.Notes;

// One paid diagnostic call on already captured test speech, in memory; never changes the saved Mongo session.
static class RecordingNoteReplay
{
    public static async Task Run(string folder)
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_WAVE1_RECORDING_LIVE") != "yes") throw new InvalidOperationException("Live diagnostic requires opt-in");
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var label = Environment.GetEnvironmentVariable("PLAYBACK_RECORDING_REPLAY_LABEL") ?? "diagnostic";
        if (!System.Text.RegularExpressions.Regex.IsMatch(label, "^[a-zA-Z0-9-]{1,40}$")) throw new InvalidOperationException("Invalid diagnostic label");
        using var snapshot = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(folder, "recovered-session.json")));
        var root = snapshot.RootElement;
        var id = root.GetProperty("id").GetString()!;
        var store = new MemoryStore(); store.Add(id); store.Languages[id] = "zh-Hant";
        var note = JsonSerializer.Deserialize<Note>(root.GetProperty("currentNote").GetRawText(), json)!;
        store.Notes[id] = note; store.Versions[id] = [note];
        store.Transcripts[id].AddRange(JsonSerializer.Deserialize<List<Transcript>>(root.GetProperty("transcripts").GetRawText(), json)!);
        using var model = new OpenRouterTextModel("qwen/qwen3.8-flash", "none");
        var writer = new Recorder(new OpenRouterNoteWriter(model), folder, label);
        var notes = new NoteStoreAdapter(store);
        await using var workflow = new NoteWorkflow(notes, notes, writer, new FixtureNoteGate(),
            new NoteActivityAdapter(new AiActivity(store), NullLogger<NoteAgent>.Instance), null, notes, notes, notes, notes, new OpenccNoteScript());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(110));
        await workflow.Generate(id, deadline.Token);
        await File.WriteAllTextAsync(Path.Combine(folder, label + "-activity.json"), JsonSerializer.Serialize(store.Activity.Values, json));
        foreach (var record in store.Activity.Values) Console.WriteLine(record.Summary);
    }
    sealed class Recorder(INoteWriter inner, string folder, string label) : INoteWriter
    {
        public string Provider => inner.Provider;
        public string Model => inner.Model;
        public bool IsConfigured => inner.IsConfigured;
        public async Task<NotePatch> Generate(string name, string instructions, string input, CancellationToken ct, Action<string>? onUpdate = null, Action<string>? onUsage = null)
        {
            var reply = "";
            await File.WriteAllTextAsync(Path.Combine(folder, label + "-input.json"), input, ct);
            try { return await inner.Generate(name, instructions, input, ct, text => { reply = text; onUpdate?.Invoke(text); }, onUsage); }
            finally { await File.WriteAllTextAsync(Path.Combine(folder, label + "-reply.json"), reply, CancellationToken.None); }
        }
        public async Task<string> Complete(NoteTask task, string instructions, string input, CancellationToken ct, Action<string>? onUpdate = null, Action<string>? onUsage = null)
        {
            var reply = "";
            await File.WriteAllTextAsync(Path.Combine(folder, label + "-input.json"), input, ct);
            try { return reply = await inner.Complete(task, instructions, input, ct, onUpdate, onUsage); }
            finally { await File.WriteAllTextAsync(Path.Combine(folder, label + "-reply.json"), reply, CancellationToken.None); }
        }
    }
}
