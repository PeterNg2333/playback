using Playback.Application.Ask;
using Playback.Application.Workspace;
using Playback.Domain.Evidence;
namespace Playback.Api.Endpoints;

// The session's concept graph (LightRAG sidecar): concepts for a question or the whole graph, each document mapped to
// the transcript rows it was built from; and a request to index a session now.
public static class ConceptEndpoints
{
    public static void MapConcepts(this WebApplication app)
    {
        app.MapGet("/api/sessions/{id}/concepts", async (string id, string? q, int? limit, IConceptGraph graph,
            WorkspaceCommands workspace, CancellationToken ct) =>
        {
            var session = await workspace.Session(id) ?? throw new WorkspaceNotFoundException();
            if (!graph.Enabled) return Results.Ok(new { enabled = false });
            var result = string.IsNullOrWhiteSpace(q) ? await graph.Graph(id, limit ?? 200, ct) : await graph.Query(id, q, ct);
            var documents = ConceptDocuments.Build(id, session.Transcripts, recording: false).ToDictionary(x => x.Id, x => x.TranscriptIds);
            return Results.Ok(new { enabled = true, result.Entities, result.Relations, result.Chunks, documents });
        });
        app.MapPost("/api/sessions/{id}/concepts/index", async (string id, IConceptGraph graph, ConceptGraphWorker worker,
            WorkspaceCommands workspace) =>
        {
            _ = await workspace.Session(id) ?? throw new WorkspaceNotFoundException();
            if (!graph.Enabled) throw new InvalidOperationException("The concept graph is off; set PLAYBACK_CONCEPT_GRAPH=yes and start the sidecar");
            worker.Request(id);
            return Results.Accepted();
        });
    }
}
