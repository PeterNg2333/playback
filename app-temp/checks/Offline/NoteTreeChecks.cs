using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Playback.Api.Activity;
using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Api.Providers;
using Playback.Application.Notes;
using Playback.Domain.Notes;
using System.Text.Json;

static class NoteTreeChecks
{
    public static async Task Run()
    {
        var store = new MemoryStore(); const string id = "tree"; store.Add(id);
        store.Speech(id, "definition", "TCP shares one bottleneck: $R/n$, only when n flows share capacity.");
        store.Speech(id, "example", "Example: n=10 gives R/10; this is an assumption, not a measured guarantee.");
        var model = new TreeModel();
        await using var agent = new NoteAgent(store, model, new FixtureNoteGate(), NullLogger<NoteAgent>.Instance, new AiActivity(store));
        await agent.Generate(id, default);
        var first = await store.NoteVersion(id, 1) ?? throw new CheckFailed("Tree version missing");
        var before = JsonSerializer.Serialize(first);
        Expect.That(first.SchemaVersion == 2 && first.Sections.Count == 3 && first.Sections.SelectMany(x => x.Blocks).Count() == 2, "Canonical tree not saved");
        Expect.That(first.Markdown.Contains("## 1 TCP") && first.Markdown.Contains("### 1.1 Shared capacity") && first.Markdown.Contains("#### 1.1.1 Worked example") && first.Markdown.Contains("$R/n$"), "Deterministic hierarchy or math missing");
        var doc = first.ToBsonDocument();
        Expect.That(doc["Sections"].AsBsonArray.All(x => !x.AsBsonDocument.Contains("Markdown") && !x.AsBsonDocument.Contains("Points")), "Canonical persistence duplicated section/point prose");
        var roundTrip = BsonSerializer.Deserialize<Note>(doc);
        Expect.That(roundTrip.Sections.SelectMany(x => x.Points).Select(x => x.Id).SequenceEqual(first.Sections.SelectMany(x => x.Points).Select(x => x.Id)), "Coverage identities lost in BSON round-trip");
        var root = first.Sections.Single(x => x.ParentId is null); var child = first.Sections.Single(x => x.ParentId == root.Id);
        var detail = first.Sections.Single(x => x.ParentId == child.Id); var block = root.Blocks.Single();
        var moved = NoteTree.Apply(first.Sections, [new() { Kind = "move-block", TopicId = child.Id, BlockId = block.Id, Order = 0 }], ["definition", "example"], "move");
        Expect.That(moved.Single(x => x.Id == child.Id).Blocks.Single().Body == block.Body && JsonSerializer.Serialize(first) == before, "Structural operation rewrote a body or mutated history");
        NoteSections.RequireRetention(first, new(moved, first.Citations, []), ["definition", "example"]);
        void Reject(List<NoteTreeOperation> operations) => Expect.Rejects(() => NoteTree.Apply(first.Sections, operations, ["definition", "example"], "invalid"), "Invalid tree operation accepted");
        Reject([new() {Kind = "move-topic", TopicId = root.Id, ParentId = detail.Id}]);
        Reject([new() {Kind = "create-topic", TopicId = "new:four", ParentId = detail.Id, Title = "Too deep"}]);
        Reject([new() {Kind = "move-block", TopicId = "unknown", BlockId = block.Id}]);
        Reject([new() {Kind = "add-block", TopicId = root.Id, BlockId = "new:bad", Body = "Unsupported [alien]", SourceIds = ["alien"]}]);
        Reject([new() {Kind = "update-block", TopicId = root.Id, BlockId = block.Id, Body = "Shorter and loses conditions"}]);
        root.UserEdited = true;
        Reject([new() {Kind = "move-topic", TopicId = root.Id, ParentId = child.Id}]);
        root.UserEdited = false;
        var commands = new NoteCommands(new NoteStoreAdapter(store), new NoteStoreAdapter(store));
        await commands.ApplyStructure(id, 1, [new() {Kind = "move-block", TopicId = child.Id, BlockId = block.Id, Order = 0}]);
        await Expect.RejectsAsync(() => commands.ApplyStructure(id, 1, [new() {Kind = "reorder-topic", TopicId = root.Id}]), "Stale outline overwrote a newer version");
        var second = store.Notes[id]; var ids = second.Sections.SelectMany(x => x.Blocks).Select(x => x.Id).ToArray();
        await commands.SaveNote(id, second.Markdown, "user", 2);
        Expect.That(store.Notes[id].Sections.SelectMany(x => x.Blocks).Select(x => x.Id).SequenceEqual(ids), "Editor round-trip changed stable block identities");
        await commands.RestoreNote(id, 1, 3);
        Expect.That(store.Notes[id].Sections.Single(x => x.Id == detail.Id).ParentId == child.Id && store.Notes[id].Sections.All(x => x.UserEdited), "Historical restore flattened the tree or lost protection");
        Expect.That(JsonSerializer.Serialize(await store.NoteVersion(id, 1)) == before, "Historical tree version changed");
        await commands.EditBlock(id, 4, block.Id, block.Body + "\n\nA user qualification.");
        Expect.That(store.Notes[id].Sections.SelectMany(x => x.Blocks).Single(x => x.Id == block.Id) is { UserEdited: true } edited && edited.Body.Contains("user qualification"), "Explicit block edit lost identity/protection");
        await Expect.RejectsAsync(() => commands.EditBlock(id, 4, block.Id, "Stale edit"), "Stale block draft overwrote a newer document");
        Console.WriteLine("Note tree checks passed: one canonical body/BSON, three-level TCP hierarchy, bounded same-call routing, stable block/coverage IDs, literal structural retention, cycles/depth/source/protection rejection, editor round-trip, stale outline and hierarchical restore");
    }
    sealed class TreeModel : GeminiLanguageModel
    {
        public override bool IsConfigured => true;
        public override Task<string> Generate(string name, string instructions, string prompt, CancellationToken ct, string? model = null, Action<string>? onUpdate = null, Action<string>? onUsage = null)
        {
            using var doc = JsonDocument.Parse(prompt); var sources = doc.RootElement.GetProperty("pending").EnumerateArray().ToArray();
            var patch = new NotePatch { Operations = [
                new() {Kind = "create-topic", TopicId = "new:tcp", Title = "TCP", Order = 0},
                new() {Kind = "create-topic", TopicId = "new:sharing", ParentId = "new:tcp", Title = "Shared capacity", Order = 0},
                new() {Kind = "create-topic", TopicId = "new:example", ParentId = "new:sharing", Title = "Worked example", Order = 0},
                new() {Kind = "add-block", TopicId = "new:tcp", BlockId = "new:definition", Role = "definition", Body = sources[0].GetProperty("text").GetString()!, SourceIds = [sources[0].GetProperty("id").GetString()!]},
                new() {Kind = "add-block", TopicId = "new:example", BlockId = "new:worked", Role = "example", Body = sources[1].GetProperty("text").GetString()!, SourceIds = [sources[1].GetProperty("id").GetString()!]},
            ] };
            return Task.FromResult(JsonSerializer.Serialize(patch));
        }
    }
}
