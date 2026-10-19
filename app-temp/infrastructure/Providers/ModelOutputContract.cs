using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Playback.Api.Notes;

namespace Playback.Api.Providers;

// What every text provider must ask for, whichever model writes the reply: a per-task output cap and, for
// note sections, a JSON schema that only admits the section IDs, source IDs and deferrals in this input.
public static class ModelOutputContract
{
    public static int OutputLimit(string name) => name is "LectureSectionWriter" or "LectureSectionOrganizer" or "LectureNoteInstruction" ? 8192 :
        name.Contains("Translator", StringComparison.Ordinal) ? 4096 : 1024;

    public static ChatResponseFormat? ResponseFormat(string name, string prompt) {
        if (name is not ("LectureSectionWriter" or "LectureSectionOrganizer" or "LectureNoteInstruction")) return null;
        var format = ChatResponseFormat.ForJsonSchema<NotePatch>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var schema = JsonNode.Parse(format.Schema!.Value.GetRawText())!;
        using var input = JsonDocument.Parse(prompt);
        var ids = input.RootElement.GetProperty("editableSections").EnumerateArray().Select(x => x.GetProperty("id").GetString()!).Prepend("new").Distinct();
        var section = schema["properties"]!["sections"]!["items"]!;
        section["properties"]!.AsObject().Remove("markdown");
        section["properties"]!.AsObject().Remove("baseVersion");
        section["required"] = new JsonArray("id", "title", "points");
        section["properties"]!["id"]!["enum"] = new JsonArray(ids.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        schema["required"] = new JsonArray("sections", "deferred");
        section["properties"]!["points"]!["items"]!["required"] = new JsonArray("text", "sourceIds", "retains");
        var root = input.RootElement;
        var inputIds = root.GetProperty("pending").EnumerateArray().Concat(root.GetProperty("materials").EnumerateArray())
            .Select(x => x.GetProperty("id").GetString()!).ToArray();
        IEnumerable<JsonElement> Rows(string key) => root.TryGetProperty(key, out var value) ? value.EnumerateArray() : [];
        var sourceIds = inputIds.Concat(Rows("sectionSources").Concat(Rows("sectionMaterials"))
                .Select(x => x.GetProperty("id").GetString()!))
            .Concat(root.GetProperty("editableSections").EnumerateArray().SelectMany(x => x.GetProperty("points").EnumerateArray())
                .SelectMany(x => x.GetProperty("sourceIds").EnumerateArray()).Select(x => x.GetString()!))
            .Concat(Rows("citationSources").Select(x => x.GetProperty("id").GetString()!))
            .Distinct().ToArray();
        var pointSources = section["properties"]!["points"]!["items"]!["properties"]!["sourceIds"]!;
        if (sourceIds.Length == 0) pointSources["maxItems"] = 0;
        else pointSources["items"]!["enum"] = new JsonArray(sourceIds.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        var deferred = schema["properties"]!["deferred"]!["items"]!;
        foreach (var key in deferred["properties"]!.AsObject().Select(x => x.Key).Where(x => x is not ("sourceId" or "reason")).ToList())
            deferred["properties"]!.AsObject().Remove(key);
        deferred["required"] = new JsonArray("sourceId", "reason");
        // A source may be deferred once; on its next turn it has to be written.
        var deferrable = root.GetProperty("pending").EnumerateArray()
            .Where(x => !(x.TryGetProperty("deferredBefore", out var before) && before.ValueKind == JsonValueKind.True))
            .Concat(root.GetProperty("materials").EnumerateArray()).Select(x => x.GetProperty("id").GetString()!).ToArray();
        if (deferrable.Length == 0) schema["properties"]!["deferred"]!["maxItems"] = 0;
        else deferred["properties"]!["sourceId"]!["enum"] = new JsonArray(deferrable.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        // Notes change through edit operations only (tree-notes-v10); explicit rewrites have their own preview task.
        schema["properties"]!["sections"]!["maxItems"] = 0;
        schema["properties"]!.AsObject().Remove("rewrites");
        // No maxItems on operations: Vertex rejects the request with it (probed 2026-10-18); NoteTree enforces 24.
        var operation = schema["properties"]!["operations"]!["items"]!;
        var organizing = name == "LectureSectionOrganizer";
        // Writing places new content through outline and blocks, so its operations only edit, move and rename.
        operation["properties"]!["kind"]!["enum"] = Strings(organizing ? StructuralKinds : EditingKinds);
        operation["properties"]!["role"]!["enum"] = Strings(BlockRoles);
        // Every operation names its topic and block ("" when the kind has none), so a move always has a target.
        var keys = Playback.Application.Notes.NoteWorkflow.NewTopicKeys;
        var topicIds = organizing ? Scope(root) : root.TryGetProperty("topicOutline", out var listed)
            ? listed.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).Select(x => x.GetProperty("id").GetString()!) : [];
        var blockIds = root.GetProperty("editableSections").EnumerateArray()
            .SelectMany(x => x.TryGetProperty("blocks", out var blocks) ? blocks.EnumerateArray() : [])
            .Select(x => x.GetProperty("id").GetString()!);
        operation["properties"]!["topicId"]!["enum"] = Strings(topicIds.Concat(keys).Prepend("").Distinct());
        operation["properties"]!["blockId"]!["enum"] = Strings(blockIds.Prepend("").Distinct());
        operation["required"] = new JsonArray("kind", "topicId", "blockId");
        var operationSources = operation["properties"]!["sourceIds"]!;
        if (sourceIds.Length == 0) operationSources["maxItems"] = 0;
        else operationSources["items"]!["enum"] = Strings(sourceIds);
        if (organizing) Regrouping(schema, root, blockIds.ToArray());
        else Placement(schema, root, sourceIds);
        return ChatResponseFormat.ForJsonSchema(JsonSerializer.SerializeToElement(schema), "LectureSectionPatch");
    }

    // Placement is a required decision, like choosing a heading while writing a document: every block names an
    // open existing topic or a new key, and a new topic names its parent. Closed topics are not offered, and a
    // parent must leave room for one more level.
    static void Placement(JsonNode schema, JsonElement root, string[] sourceIds)
    {
        var topics = root.TryGetProperty("topicOutline", out var outline) ? outline.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object).ToList() : [];
        var open = topics.Where(x => !(x.TryGetProperty("closed", out var closed) && closed.ValueKind == JsonValueKind.True) &&
            !(x.TryGetProperty("userEdited", out var locked) && locked.ValueKind == JsonValueKind.True)).Select(x => x.GetProperty("id").GetString()!);
        var parents = topics.Where(x => (!x.TryGetProperty("depth", out var depth) || depth.GetInt32() < 3) &&
            !(x.TryGetProperty("recent", out var recent) && recent.ValueKind == JsonValueKind.False)).Select(x => x.GetProperty("id").GetString()!);
        var keys = Playback.Application.Notes.NoteWorkflow.NewTopicKeys;
        var item = schema["properties"]!["outline"]!["items"]!;
        item["properties"]!["key"]!["enum"] = Strings(keys);
        item["properties"]!["parent"]!["enum"] = Strings(parents.Prepend("").Concat(keys));
        item["required"] = new JsonArray("key", "title", "parent");
        var block = schema["properties"]!["blocks"]!["items"]!;
        block["properties"]!["topic"]!["enum"] = Strings(open.Concat(keys));
        block["properties"]!["role"]!["enum"] = Strings(BlockRoles);
        if (sourceIds.Length == 0) block["properties"]!["sourceIds"]!["maxItems"] = 0;
        else block["properties"]!["sourceIds"]!["items"]!["enum"] = Strings(sourceIds);
        block["required"] = new JsonArray("topic", "role", "body", "sourceIds");
        schema["properties"]!.AsObject().Remove("moves");
        schema["properties"]!.AsObject().Remove("topicMoves");
        schema["required"] = new JsonArray("outline", "blocks", "deferred");
    }

    // Organization declares its new topics the same way, as outline keys under a topic in the selected scope, then
    // places existing blocks in moves and topics in topicMoves, each naming where it goes; it never writes blocks.
    // Every destination is required and is a selected topic or a new key, so none can be missing or outside the
    // scope: Qwen left move-block's topicId out or named the block's current topic (tree-organize-v13), and left
    // move-topic's optional parentId out of every regroup (v14). Operations only rename and reorder. Topic IDs come
    // from editableSections, or from the outline rows of an outline-only request, which has no blocks to move.
    static void Regrouping(JsonNode schema, JsonElement root, string[] blockIds)
    {
        var scope = Scope(root).ToArray();
        var keys = Playback.Application.Notes.NoteWorkflow.NewTopicKeys;
        var item = schema["properties"]!["outline"]!["items"]!;
        item["properties"]!["key"]!["enum"] = Strings(keys);
        item["properties"]!["parent"]!["enum"] = Strings(scope.Prepend("").Concat(keys));
        item["required"] = new JsonArray("key", "title", "parent");
        schema["properties"]!.AsObject().Remove("blocks");
        var moves = schema["properties"]!["moves"]!;
        if (blockIds.Length == 0) moves["maxItems"] = 0;
        else moves["items"]!["properties"]!["block"]!["enum"] = Strings(blockIds);
        moves["items"]!["properties"]!["to"]!["enum"] = Strings(scope.Concat(keys));
        moves["items"]!["required"] = new JsonArray("block", "to");
        var topicMoves = schema["properties"]!["topicMoves"]!["items"]!;
        if (scope.Length == 0) schema["properties"]!["topicMoves"]!["maxItems"] = 0;
        else topicMoves["properties"]!["topic"]!["enum"] = Strings(scope);
        topicMoves["properties"]!["to"]!["enum"] = Strings(scope.Concat(keys));
        topicMoves["required"] = new JsonArray("topic", "to");
        schema["properties"]!["operations"]!["items"]!["properties"]!["kind"]!["enum"] = Strings(blockIds.Length == 0 ? ["rename-topic", "reorder-topic"] : StructuralKinds);
        schema["required"] = new JsonArray("outline", "moves", "topicMoves", "operations", "deferred");
    }

    static IEnumerable<string> Scope(JsonElement root) => root.GetProperty("editableSections").EnumerateArray().Select(x => x.GetProperty("id").GetString()!)
        .Concat(root.TryGetProperty("topicOutline", out var outline) ? outline.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Array).Select(x => x[0].GetString()!) : []).Distinct();

    static readonly string[] StructuralKinds = ["rename-topic", "reorder-topic", "reorder-block"];
    static readonly string[] EditingKinds = ["rename-topic", "move-topic", "reorder-topic", "move-block", "reorder-block", "replace-text", "delete-block"];
    static readonly string[] BlockRoles = ["definition", "example", "derivation", "correction", "detail"];
    static JsonArray Strings(IEnumerable<string> values) => new(values.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
}
