using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;
using Playback.Api.Activity;
using Playback.Api.Endpoints;
using Playback.Api.Notes;

// Invoke the real minimal-API delegates in memory: no listener, MongoDB, provider, files or audio.
static class NoteApiChecks
{
    public static async Task Run()
    {
        var store = new MemoryStore();
        store.Add("api-fixture");
        store.Speech("api-fixture", "source-a", "R/n assumes one shared bottleneck.");
        var adapter = new NoteStoreAdapter(store);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<Playback.Api.Db.PlaybackStore>(store);
        builder.Services.AddSingleton<Playback.Application.Workspace.IWorkspaceRepository>(store);
        builder.Services.AddSingleton<Playback.Application.Workspace.WorkspaceCommands>();
        builder.Services.AddSingleton(new NoteCommands(adapter, adapter, adapter));
        builder.Services.AddSingleton(new NoteWorkflow(adapter, adapter, new GeminiNoteWriter(new FixtureNoteModel()), new FixtureNoteGate(),
            new NoteActivityAdapter(new AiActivity(store), NullLogger<NoteAgent>.Instance)));
        await using var app = builder.Build();
        app.MapNotes();
        app.MapProcessingSettings();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints).Cast<RouteEndpoint>().ToList();
        async Task<(int Status, JsonElement Body)> Request(string pattern, string method = "GET", string? body = null, int? version = null, string? proposalId = null)
        {
            var endpoint = endpoints.Single(x => x.RoutePattern.RawText == pattern &&
                x.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains(method));
            var context = new DefaultHttpContext { RequestServices = app.Services };
            context.Request.Method = method;
            context.Request.RouteValues["id"] = "api-fixture";
            if (proposalId is not null) context.Request.RouteValues["proposalId"] = proposalId;
            if (version is not null) context.Request.RouteValues["version"] = version.Value.ToString();
            context.Response.Body = new MemoryStream();
            if (body is not null)
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                context.Request.Body = new MemoryStream(bytes);
                context.Request.ContentType = "application/json";
                context.Request.ContentLength = bytes.Length;
                context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature>(new BodyDetection());
            }
            await new ApiExceptionMiddleware(endpoint.RequestDelegate!).InvokeAsync(context);
            context.Response.Body.Position = 0;
            if (context.Response.Body.Length == 0) return (context.Response.StatusCode, default);
            using var json = await JsonDocument.ParseAsync(context.Response.Body);
            return (context.Response.StatusCode, json.RootElement.Clone());
        }

        const string root = "/api/sessions/{id}/notes";
        const string settings = "/api/sessions/{id}/processing-settings";
        var inherited = await Request(settings);
        Expect.That(inherited.Body.GetProperty("effective").GetProperty("maxPendingWaitSeconds").GetDouble() == 30, "Settings API lost server defaults");
        var configured = await Request(settings, "PUT", "{\"expectedRevision\":0,\"overrides\":{\"jevEnabled\":false,\"maxPendingWaitSeconds\":10}}");
        Expect.That(configured.Status == 200 && !configured.Body.GetProperty("effective").GetProperty("jevEnabled").GetBoolean(), "Settings API lost explicit false");
        var invalid = await Request(settings, "PUT", "{\"expectedRevision\":1,\"overrides\":{\"maxPendingWaitSeconds\":9}}");
        Expect.That(invalid.Status == 400 && (await store.SessionProcessingSettings("api-fixture")).Revision == 1, "Invalid policy changed saved settings");
        var unknown = await Request(settings, "PUT", "{\"expectedRevision\":1,\"overrides\":{\"maxPendingWaitSecond\":20}}");
        Expect.That(unknown.Status == 400, "Unknown setting silently reset inheritance");
        var stale = await Request(settings, "PUT", "{\"expectedRevision\":0,\"overrides\":{}}");
        Expect.That(stale.Status == 409 && (await store.SessionProcessingSettings("api-fixture")).Revision == 1, "Stale settings silently overwrote a newer tab");
        var workspace = await Request("/api/settings/processing", "PUT", "{\"expectedRevision\":0,\"overrides\":{\"checkIntervalSeconds\":2}}");
        Expect.That(workspace.Status == 200 && (await store.SessionProcessingSettings("api-fixture")).Effective.CheckIntervalSeconds == 2, "Workspace API defaults did not reach an inherited session field");
        var saved = await Request(root, "POST", "{\"markdown\":\"## Capacity\\n\\nR/n assumes one shared bottleneck. [source-a]\",\"basedOnVersion\":0}");
        Expect.That(saved.Status == 200 && saved.Body.GetProperty("version").GetInt32() == 1, "Notes API no longer saves a user version");
        var conflict = await Request(root, "POST", "{\"markdown\":\"Stale edit\",\"basedOnVersion\":0}");
        Expect.That(conflict.Status == 409 && store.Notes["api-fixture"].Version == 1, "Stale API edit overwrote saved notes");
        var version = await Request(root + "/{version:int}", version: 1);
        var history = await Request(root);
        Expect.That(version.Status == 200 && history.Body.GetArrayLength() == 1 &&
            version.Body.GetProperty("sections")[0].GetProperty("userEdited").GetBoolean(), "Saved note/history contract lost section protection");
        var coverage = await Request(root + "/coverage");
        Expect.That(coverage.Body.GetProperty("referenced").GetInt32() == 1, "API coverage lost a persistent source link");
        var missing = await Request(root + "/{version:int}", version: 99);
        Expect.That(missing.Status == 404, "Missing historical version stopped returning 404");

        await Request(root, "POST", "{\"markdown\":\"## User correction\\n\\nAn explicit replacement.\",\"basedOnVersion\":1}");
        var recovery = await Request(root + "/{version:int}/recovery", version: 1);
        Expect.That(recovery.Body.GetProperty("candidates").GetArrayLength() == 0, "Recovery revived an intentional user deletion");
        var restored = await Request(root + "/{version:int}/restore", "POST", version: 1);
        Expect.That(restored.Status == 200 && restored.Body.GetProperty("version").GetInt32() == 3 &&
            store.Notes["api-fixture"].Markdown.Contains("R/n"), "Restore did not append a protected version");
        var edits = await Request(root + "/edits");
        Expect.That(edits.Body.GetArrayLength() == 3 && (await Request(root)).Body[0].GetProperty("version").GetInt32() == 1,
            "Restore changed the first historical version or edit history order");
        store.Transcripts["api-fixture"][0].NoteStatus = "completed";
        store.Speech("api-fixture", "source-b", "Ten flows target R/10 under that assumption.");
        var generated = await Request(root + "/generate", "POST");
        Expect.That(generated.Status == 200 && generated.Body.GetProperty("version").GetInt32() == 4 &&
            store.Notes["api-fixture"].Sections[0].UserEdited && store.Notes["api-fixture"].Markdown.Contains("R/10"),
            "Generate API lost protected user content or its new source");
        var selected = store.Notes["api-fixture"].Sections.Single(x => !x.UserEdited);
        var organized = await Request(root + "/organize", "POST", JsonSerializer.Serialize(new { sectionId = selected.Id, basedOnVersion = 4 }));
        Expect.That(organized.Status == 200 && organized.Body.GetProperty("version").GetInt32() == 5 &&
            store.Notes["api-fixture"].Markdown.Contains("R/n") && store.Notes["api-fixture"].Markdown.Contains("R/10"), "Organize API lost a saved point: " + organized.Body);
        var noGap = await Request(root + "/repair", "POST", "{\"basedOnVersion\":5}");
        Expect.That(noGap.Status == 409 && store.Notes["api-fixture"].Version == 5, "Repair API wrote a version without an actionable gap");
        var moved = await Request(root + "/structure", "POST", JsonSerializer.Serialize(new {basedOnVersion = 5,
            operations = new[] {new {kind = "move-topic", topicId = selected.Id, parentId = store.Notes["api-fixture"].Sections[0].Id, order = 0}} }));
        Expect.That(moved.Status == 200 && store.Notes["api-fixture"].Version == 6 && store.Notes["api-fixture"].Markdown.Contains("R/n") && store.Notes["api-fixture"].Markdown.Contains("R/10"), "Outline API lost a saved body");
        var selectedBlock = store.Notes["api-fixture"].Sections.Single(x => x.Id == selected.Id).Blocks.Single();
        var blockEdit = await Request(root + "/block", "POST", JsonSerializer.Serialize(new {basedOnVersion = 6, blockId = selectedBlock.Id, body = selectedBlock.Body + "\n\nA user qualification."}));
        Expect.That(blockEdit.Status == 200 && store.Notes["api-fixture"].Version == 7 && store.Notes["api-fixture"].Sections.SelectMany(x => x.Blocks).Single(x => x.Id == selectedBlock.Id).UserEdited, "Block edit API lost stable identity/protection");
        var current = store.Notes["api-fixture"];
        var proposal = Playback.Domain.Notes.NoteRewrite.Preview(current, selected.Id, [new() {TargetBlockId = selectedBlock.Id, RetainedBlockIds = [selectedBlock.Id], Body = selectedBlock.Body + "\n\nA user qualification.\n\nReviewed context."}], ["source-a", "source-b"], DateTime.UtcNow);
        await store.SaveNoteRewrite(proposal);
        var previews = await Request(root + "/rewrites");
        Expect.That(previews.Status == 200 && previews.Body.GetArrayLength() == 1, "Rewrite preview query lost the saved proposal");
        var declined = await Request(root + "/rewrites/{proposalId}/accept", "POST", "{\"semanticAccepted\":false}", proposalId: proposal.Id);
        Expect.That(declined.Status == 409 && store.Notes["api-fixture"].Version == 7, "Rewrite API accepted without semantic review");
        var accepted = await Request(root + "/rewrites/{proposalId}/accept", "POST", "{\"semanticAccepted\":true}", proposalId: proposal.Id);
        Expect.That(accepted.Status == 200 && store.Notes["api-fixture"].Version == 8 && store.Notes["api-fixture"].Markdown.Contains("Reviewed context"), "Rewrite API did not apply the retained preview: " + accepted.Body);
        Console.WriteLine("Notes API checks passed in memory: save/conflict, coverage, version/history, deletion, restore, edits, generation, outline, block edit and explicitly accepted rewrite");
    }

    sealed class BodyDetection : Microsoft.AspNetCore.Http.Features.IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
