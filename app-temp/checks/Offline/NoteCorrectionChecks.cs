using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Playback.Api.Activity;
using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Api.Providers;
using Playback.Domain;

static class NoteCorrectionChecks
{
    public static async Task Run()
    {
        var store = new MemoryStore(); store.Add("correction");
        store.Speech("correction", "source", "The cash holds recently used data.");
        var model = new CorrectionModel();
        await using var agent = new NoteAgent(store, model, new FixtureNoteGate(), NullLogger<NoteAgent>.Instance, new AiActivity(store));
        await agent.Generate("correction", default);
        var before = store.Notes["correction"];
        var source = store.Transcripts["correction"].Single();
        await agent.UpdateEvidence("correction", () =>
        {
            source.FirstPassOriginal = source.Original;
            source.Original = "The cache holds recently used data.";
            source.NoteStatus = "pending";
            return Task.FromResult(true);
        }, default);
        model.Correct = true;
        await agent.Generate("correction", default);
        var after = store.Notes["correction"];
        Expect.That(after.Sections.SelectMany(x => x.Blocks).Count() == 1 && after.Markdown.Contains("cache holds") && !after.Markdown.Contains("cash holds") &&
            after.Sections.Single().Blocks.Single().Id == before.Sections.Single().Blocks.Single().Id &&
            source.Id == "source" && source.FirstPassOriginal?.Contains("cash") == true && source.NoteStatus == "completed" &&
            after.Coverage.Single().ContentHash == ContentHash.Of(source.SourceText),
            "Corrected speech must edit the existing cited block, keep first-pass text and stable IDs, and acknowledge the corrected source hash");

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        model.BeforeFinish = () => {entered.SetResult(); return release.Task;};
        var writing = agent.Generate("correction", default);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var applied = false;
        var correction = agent.UpdateEvidence("correction", () => {applied = true; return Task.FromResult(true);}, default);
        Expect.That(!applied, "A correction raced a note call still acknowledging old source text");
        release.SetResult(); await writing; await correction;
        Expect.That(applied, "Evidence correction did not resume after note processing");
        Console.WriteLine("Note correction checks passed: corrected ASR edits the existing cited block, stable source/block identities and corrected coverage hash, retained first pass, serialized note/correction writes (fixture model only)");
    }
    sealed class CorrectionModel : GeminiLanguageModel
    {
        readonly FixtureNoteModel initial = new();
        public bool Correct;
        public Func<Task>? BeforeFinish;
        public override bool IsConfigured => true;
        public override async Task<string> Generate(string name, string instructions, string prompt, CancellationToken ct, string? model = null,
            Action<string>? onUpdate = null, Action<string>? onUsage = null)
        {
            if (!Correct) return await initial.Generate(name, instructions, prompt, ct, model, onUpdate, onUsage);
            if (BeforeFinish is not null) {await BeforeFinish(); return "{\"operations\":[],\"blocks\":[],\"outline\":[],\"sections\":[],\"deferred\":[]}";}
            using var doc = JsonDocument.Parse(prompt);
            var root = doc.RootElement;
            Expect.That(root.GetProperty("pending")[0].GetProperty("retranscribed").GetBoolean(), "The writer was not told this source is a second-pass correction");
            var block = root.GetProperty("editableSections")[0].GetProperty("blocks")[0].GetProperty("id").GetString()!;
            return JsonSerializer.Serialize(new NotePatch {Operations = [new() {Kind = "replace-text", BlockId = block, TopicId = "", Find = "cash", Replace = "cache", Reason = "Corrected audio recognition"}]});
        }
    }
}
