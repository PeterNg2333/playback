using System.Net;
using System.Text;
using Playback.Api.Activity;
using Playback.Api.Ask;
using Playback.Api.Providers;
using Playback.Domain.Evidence;
using Playback.Domain.Models;

static class ConceptGraphChecks
{
    public static async Task Run()
    {
        Documents();
        await Index();
        await Client();
        await Ask();
        Console.WriteLine("Concept graph checks passed: one-minute documents from active rows only, open minute held while recording, changed hash re-sent and " +
            "removed bucket deleted, fixed loopback endpoint with size/redirect/offline/off guards, Ask reads graph passages first and falls back to keywords " +
            "when the graph fails (in-memory HTTP and fixture graph only)");
    }

    static Transcript Row(string id, long start, string text, string source = "mic") =>
        new() { Id = id, SessionId = "s", SourceId = source, StartMs = start, EndMs = start + 8000, Original = text };

    static void Documents()
    {
        var rows = new List<Transcript> { Row("a", 0, "Cache lines"), Row("b", 30_000, "hold data"), Row("c", 65_000, "Misses go to memory"),
            Row("old", 70_000, "superseded") };
        rows[3].SupersededBy = "c";
        var stopped = ConceptDocuments.Build("s", rows, recording: false);
        Expect.That(stopped.Count == 2 && stopped[0].Id == "s-concept-mic-0" && stopped[0].Text == "Cache lines\nhold data" &&
            stopped[0].TranscriptIds.SequenceEqual(new[] { "a", "b" }) && stopped[1].TranscriptIds.SequenceEqual(new[] { "c" }),
            "Documents must hold one minute of active rows per source");
        Expect.That(ConceptDocuments.Build("s", rows, recording: true).Single().Id == "s-concept-mic-0", "The minute still being recorded must wait");
        rows[1].Original = "keep data";
        Expect.That(ConceptDocuments.Build("s", rows, false)[0].Hash != stopped[0].Hash, "A rerun that changes wording must change the document hash");
    }

    static async Task Index()
    {
        var store = new MemoryStore(); store.Add("idx");
        store.Transcripts["idx"].AddRange([Row("a", 0, "Cache lines"), Row("b", 65_000, "Misses")]);
        var graph = new FakeGraph();
        var index = new ConceptIndex(graph, new AiActivity(store));
        await index.Sync((await store.Session("idx"))!, false, default);
        await index.Sync((await store.Session("idx"))!, false, default);
        Expect.That(graph.Upserts.Count == 1 && graph.Upserts[0].Count == 2, "Unchanged documents must not be sent again");
        store.Transcripts["idx"][0].SupersededBy = "w";
        store.Transcripts["idx"].Add(Row("w", 130_000, "Window text"));
        store.Transcripts["idx"][0].Original = "Cache lines";
        store.Transcripts["idx"][1].Original = "Misses go to memory";
        await index.Sync((await store.Session("idx"))!, false, default);
        Expect.That(graph.Upserts.Count == 2 && graph.Upserts[1].Select(x => x.Id).Order().SequenceEqual(new[] { "idx-concept-mic-1", "idx-concept-mic-2" }) &&
            graph.Deleted.SequenceEqual(new[] { "idx-concept-mic-0" }) &&
            store.Activity.Values.Count(x => x.Task == "Concept graph index" && x.Status == "completed") == 2,
            "Only changed documents may be re-sent, and an emptied minute must be deleted");
        graph.Enabled = false;
        store.Transcripts["idx"][1].Original = "changed again";
        await index.Sync((await store.Session("idx"))!, false, default);
        Expect.That(graph.Upserts.Count == 2, "A disabled graph must not be called");
    }

    static async Task Client()
    {
        var previous = (Environment.GetEnvironmentVariable("PLAYBACK_CONCEPT_GRAPH"), Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST"));
        var session = new string('a', 32);
        try
        {
            Environment.SetEnvironmentVariable("PLAYBACK_CONCEPT_GRAPH", null);
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", null);
            var handler = new Handler();
            using var client = new LightRagClient(handler);
            await Expect.RejectsAsync(() => client.Query(session, "q", default), "A graph that is off must refuse requests");
            Environment.SetEnvironmentVariable("PLAYBACK_CONCEPT_GRAPH", "yes");
            handler.Body = """{"entities":[{"name":"Cache","type":"concept","description":"Fast memory","documentIds":["d1"]}],"relations":[],"chunks":[{"documentId":"d1","content":"Cache lines"}]}""";
            var result = await client.Query(session, "What is a cache?", default);
            Expect.That(result.Entities.Single().Name == "Cache" && result.Chunks.Single().DocumentId == "d1" &&
                handler.Requests.Single().RequestUri!.ToString() == $"http://127.0.0.1:5090/sessions/{session}/query", "Client must call the fixed loopback endpoint");
            await Expect.RejectsAsync(() => client.Query("../other", "q", default), "Client must refuse an invalid session identity");
            handler.Body = new string('x', LightRagClient.ResponseLimit + 1);
            await Expect.RejectsAsync(() => client.Query(session, "q", default), "Client must refuse an oversized response");
            handler.Status = HttpStatusCode.Redirect; handler.Body = "{}";
            await Expect.RejectsAsync(() => client.Query(session, "q", default), "Client must refuse a redirect");
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "yes");
            await Expect.RejectsAsync(() => client.Query(session, "q", default), "Offline tests must never reach the sidecar");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLAYBACK_CONCEPT_GRAPH", previous.Item1);
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", previous.Item2);
        }
    }

    static async Task Ask()
    {
        var store = new MemoryStore(); store.Add("ask");
        store.Transcripts["ask"].Add(Row("target", 0, "The capacitor stores charge between two plates."));
        for (var i = 1; i <= 8; i++) store.Transcripts["ask"].Add(Row("other" + i, i * 70_000, $"Routers forward packets, part {i}."));
        var question = "What did the lecturer say about energy storage?";
        var graph = new FakeGraph { Result = new([], [], [new(ConceptDocuments.IdFor("ask", "mic", 0), "The capacitor stores charge")]) };
        var model = new PromptModel();
        await new ChatAgent(store, model, new AiActivity(store), graph).Ask("ask", new(question), default);
        Expect.That(model.Prompt.Contains("capacitor stores charge") && graph.Queries == 1 &&
            store.Activity.Values.Any(x => x.Task == "Concept graph query" && x.Status == "completed" && x.SourceIds.Contains("target")),
            "Ask must read the passage the concept graph found, ahead of keyword ranking");
        var failing = new FakeGraph { Fail = true };
        var fallback = new PromptModel();
        await new ChatAgent(store, fallback, new AiActivity(store), failing).Ask("ask", new(question + " "), default);
        Expect.That(!fallback.Prompt.Contains("capacitor") && fallback.Prompt.Contains("Routers forward packets") &&
            store.Activity.Values.Any(x => x.Task == "Concept graph query" && x.Status == "failed"), "A failing graph must fall back to keyword retrieval and say so");
    }

    sealed class FakeGraph : IConceptGraph
    {
        public bool Enabled { get; set; } = true;
        public bool Fail; public int Queries;
        public ConceptResult Result = ConceptResult.Empty;
        public readonly List<List<ConceptDocument>> Upserts = [];
        public readonly List<string> Deleted = [];
        public Task<ConceptUpsert> Upsert(string sessionId, string language, IReadOnlyList<ConceptDocument> documents, CancellationToken ct)
        { Upserts.Add(documents.ToList()); return Task.FromResult(new ConceptUpsert(documents.Count, 0, 0, [])); }
        public Task Delete(string sessionId, IReadOnlyList<string> documentIds, CancellationToken ct) { Deleted.AddRange(documentIds); return Task.CompletedTask; }
        public Task<ConceptResult> Query(string sessionId, string question, CancellationToken ct)
        { Queries++; return Fail ? throw new HttpRequestException("Sidecar unavailable") : Task.FromResult(Result); }
        public Task<ConceptResult> Graph(string sessionId, int limit, CancellationToken ct) => Task.FromResult(Result);
    }
    sealed class PromptModel : GeminiLanguageModel
    {
        public string Prompt = "";
        public override Task<string> Generate(string name, string instructions, string prompt, CancellationToken ct,
            string? model = null, Action<string>? onUpdate = null, Action<string>? onUsage = null)
        { Prompt = prompt; return Task.FromResult("Answer"); }
    }
    sealed class Handler : HttpMessageHandler
    {
        public string Body = "{}"; public HttpStatusCode Status = HttpStatusCode.OK; public readonly List<HttpRequestMessage> Requests = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Requests.Add(request); return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") }); }
    }
}
