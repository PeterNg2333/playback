using System.Net;
using System.Text;
using System.Text.Json;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Playback.Api.Notes;
using Playback.Api.Providers;

// Gemini: the real SDK request keeps its token cap and section schema, a truncated reply is rejected with
// its usage kept, grounded search keeps only safe web sources, and every task keeps its cost limit.
static class GeminiChecks
{
    public static async Task Run()
    {
        await TruncatedReplies();
        GroundedSearch();
        Expect.That(GeminiLanguageModel.OutputLimit("LectureSectionWriter") == 8192 && GeminiLanguageModel.OutputLimit("PlaybackTranslator") == 4096 &&
            GeminiLanguageModel.OutputLimit("PlaybackQuestionAnswerer") == 1024 && NoteScheduler.Interval.TotalSeconds == 10,
            "Automatic notes and routine answers must retain explicit cost limits");
        Console.WriteLine("Gemini checks passed (in-memory HTTP): SDK token cap and section schema, truncation rejected with usage kept, safe grounded sources, cost limits");
    }

    static async Task TruncatedReplies()
    {
        using var handler = new FixtureHandler();
        using var provider = new Client(vertexAI: true, apiKey: "offline-fixture-key",
            clientOptions: new ClientOptions { HttpClientFactory = () => new HttpClient(handler, disposeHandler: false) });
        var usage = new List<string>();
        using var chat = new OutputGuardChatClient(provider.AsIChatClient(GeminiLanguageModel.DefaultModel), usage.Add);
        var agent = new ChatClientAgent(chat, new ChatClientAgentOptions
        {
            Name = "OfflineOutputCheck",
            ChatOptions = new ChatOptions { MaxOutputTokens = 1024 }
        });
        Expect.That((await agent.RunAsync("Question")).ToString().Contains("Complete answer"), "A complete routine response was rejected");
        handler.Truncated = true;
        try
        {
            await agent.RunAsync("Question");
            throw new CheckFailed("A truncated non-streaming response was accepted");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("incomplete response")) { }
        try
        {
            await foreach (var update in agent.RunStreamingAsync("Question")) { }
            throw new CheckFailed("A truncated streaming response was accepted");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("incomplete response")) { }
        Expect.That(handler.Requests == 3, "Unexpected Gemini retry or missing provider call");
        Expect.That(usage.Count == 3 && usage.All(x => x.Contains("InputTokenCount")), "Rejected paid completions lost reported usage");

        handler.Truncated = false;
        handler.Structured = true;
        var sections = new ChatClientAgent(chat, new ChatClientAgentOptions
        {
            Name = "OfflineSectionSchemaCheck",
            ChatOptions = new ChatOptions
            {
                MaxOutputTokens = 8192,
                ResponseFormat = GeminiLanguageModel.ResponseFormat("LectureSectionWriter", """
                    {"pending":[{"id":"T001"}],"materials":[{"id":"T002"}],
                    "sectionSources":[{"id":"T003"}],"sectionMaterials":[{"id":"T004"}],
                    "editableSections":[{"id":"known-section","points":[{"sourceIds":["T003","T004"]}]}],
                    "citationSources":[{"id":"cite_existing"}]}
                    """)
            }
        });
        await foreach (var update in sections.RunStreamingAsync("Confirmed lecture input")) { }
        Expect.That(handler.Requests == 4 && usage.Count == 4, "Structured response validation lost usage or repeated a call");
        handler.EmptyInput = true;
        var organizer = new ChatClientAgent(chat, new ChatClientAgentOptions
        {
            Name = "OfflineOrganizerSchemaCheck",
            ChatOptions = new ChatOptions { MaxOutputTokens = 8192,
                ResponseFormat = GeminiLanguageModel.ResponseFormat("LectureSectionOrganizer", """
                    {"pending":[],"materials":[],"sectionSources":[],"sectionMaterials":[],
                    "editableSections":[{"id":"known-section","points":[]}],"citationSources":[]}
                    """) }
        });
        await foreach (var update in organizer.RunStreamingAsync("User-authored uncited text")) { }
        Expect.That(handler.Requests == 5 && usage.Count == 5, "An input-free organizer repeated a request or lost usage");
    }

    static void GroundedSearch()
    {
        var grounded = GeminiLanguageModel.ParseGrounding(Json("""
            {"candidates":[{"content":{"parts":[{"text":"Acoustic features summarize a waveform."}]},"groundingMetadata":{"groundingChunks":[{"web":{"uri":"https://example.org/source","title":"Example source"}},{"web":{"uri":"javascript:alert(1)","title":"Unsafe"}}],"searchEntryPoint":{"renderedContent":"<div>Search suggestions</div>"}}}]}
            """));
        Expect.That(grounded.Answer == "Acoustic features summarize a waveform.", "Grounded answer text was lost");
        Expect.That(grounded.Evidence.Count == 1, "Unsafe or missing citation was accepted");
        Expect.That(grounded.Evidence[0].Url == "https://example.org/source" && grounded.SearchSuggestions.Contains("Search suggestions"),
            "Vertex citation or search suggestions were lost");
        Expect.Rejects(() => GeminiLanguageModel.ParseGrounding(Json("{\"candidates\":[]}")), "Empty grounded response was accepted");
        Expect.Rejects(() => GeminiLanguageModel.ParseGrounding(Json(
            "{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\",\"content\":{\"parts\":[{\"text\":\"Partial answer\"}]}}]}")),
            "A truncated grounded answer was accepted");
        string? rejectedGroundingUsage = null;
        Expect.Rejects(() => GeminiLanguageModel.ParseGrounding(Json(
            "{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\"}],\"usageMetadata\":{\"promptTokenCount\":20,\"candidatesTokenCount\":10}}"),
            value => rejectedGroundingUsage = value), "Truncated grounding was accepted");
        Expect.That(rejectedGroundingUsage?.Contains("promptTokenCount") == true, "Rejected grounding lost reported usage");
    }

    static byte[] Json(string value) => Encoding.UTF8.GetBytes(value);

    // Checks every request the SDK sends and answers with a complete or a truncated reply.
    sealed class FixtureHandler : HttpMessageHandler
    {
        public bool Truncated { get; set; }
        public bool Structured { get; set; }
        public bool EmptyInput { get; set; }
        public int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var config = body.RootElement.GetProperty("generationConfig");
            Expect.That(config.GetProperty("maxOutputTokens").GetInt32() == (Structured ? 8192 : 1024), "The SDK request lost the configured output cap");
            if (Structured)
            {
                Expect.That(config.GetProperty("responseMimeType").GetString() == "application/json", "Notes did not request structured JSON");
                var schema = config.TryGetProperty("responseJsonSchema", out var jsonSchema) ? jsonSchema : config.GetProperty("responseSchema");
                var section = schema.GetProperty("properties").GetProperty("sections").GetProperty("items").GetProperty("properties");
                var ids = section.GetProperty("id").GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToArray();
                Expect.That(ids.SequenceEqual(new[] { "new", "known-section" }), "The SDK section schema accepted an invented identity");
                Expect.That(!section.TryGetProperty("markdown", out _), "The provider contract still asks for a second authored copy of the same prose");
                Expect.That(!section.TryGetProperty("baseVersion", out _), "The provider contract asks a model to reproduce an already captured concurrency version");
                var pointSources = section.GetProperty("points").GetProperty("items").GetProperty("properties").GetProperty("sourceIds");
                var deferred = schema.GetProperty("properties").GetProperty("deferred");
                if (EmptyInput)
                    Expect.That(pointSources.GetProperty("maxItems").GetInt32() == 0 && deferred.GetProperty("maxItems").GetInt32() == 0,
                        "An organizer without source input can invent source IDs or deferrals");
                else
                {
                    Expect.That(pointSources.GetProperty("items").GetProperty("enum").EnumerateArray().Select(x => x.GetString())
                            .SequenceEqual(new[] { "T001", "T002", "T003", "T004", "cite_existing" }),
                        "The SDK schema failed to constrain point sources to supplied aliases and saved citations");
                    Expect.That(deferred.GetProperty("items").GetProperty("properties").GetProperty("sourceId").GetProperty("enum")
                            .EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "T001", "T002" }),
                        "The SDK schema permits deferring unknown sources or editable context");
                }
            }
            var response = JsonSerializer.Serialize(new
            {
                candidates = new[]
                {
                    new { content = new { role = "model", parts = new[] { new { text = "Complete answer" } } }, finishReason = Truncated ? "MAX_TOKENS" : "STOP" }
                },
                usageMetadata = new { promptTokenCount = 20, candidatesTokenCount = 10, totalTokenCount = 30 }
            });
            var stream = request.RequestUri!.AbsolutePath.Contains("streamGenerateContent");
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(stream ? "data: " + response + "\n\n" : response, Encoding.UTF8, stream ? "text/event-stream" : "application/json")
            };
        }
    }
}
