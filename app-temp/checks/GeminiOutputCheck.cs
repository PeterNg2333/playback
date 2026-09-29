using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Playback.Api.Services.Ai.Providers;
using System.Net;
using System.Text;
using System.Text.Json;

public static class GeminiOutputCheck
{
    public static async Task Run()
    {
        using var handler = new FixtureHandler();
        using var provider = new Client(vertexAI: true, apiKey: "offline-fixture-key",
            clientOptions: new ClientOptions { HttpClientFactory = () => new HttpClient(handler, disposeHandler: false) });
        var usage = new List<string>();
        using var chat = new OutputGuardChatClient(provider.AsIChatClient(GeminiLanguageModel.DefaultModel), usage.Add);
        var agent = new ChatClientAgent(chat, new ChatClientAgentOptions {
            Name = "OfflineOutputCheck", ChatOptions = new ChatOptions { MaxOutputTokens = 1024 }
        });
        if (!(await agent.RunAsync("Question")).ToString().Contains("Complete answer"))
            throw new Exception("A complete routine response was rejected");
        handler.Truncated = true;
        try { await agent.RunAsync("Question"); throw new Exception("A truncated non-streaming response was accepted"); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("incomplete response")) { }
        try {
            await foreach (var update in agent.RunStreamingAsync("Question")) { }
            throw new Exception("A truncated streaming response was accepted");
        } catch (InvalidOperationException ex) when (ex.Message.Contains("incomplete response")) { }
        if (handler.Requests != 3) throw new Exception("Unexpected Gemini retry or missing provider call");
        if (usage.Count != 3 || usage.Any(x => !x.Contains("InputTokenCount"))) throw new Exception("Rejected paid completions lost reported usage");
        handler.Truncated = false; handler.Structured = true;
        var sections = new ChatClientAgent(chat, new ChatClientAgentOptions {
            Name = "OfflineSectionSchemaCheck", ChatOptions = new ChatOptions { MaxOutputTokens = 8192,
                ResponseFormat = GeminiLanguageModel.ResponseFormat("LectureSectionWriter", "{\"editableSections\":[{\"id\":\"known-section\"}]}") }
        });
        await foreach (var update in sections.RunStreamingAsync("Confirmed lecture input")) { }
        if (handler.Requests != 4 || usage.Count != 4) throw new Exception("Structured response validation lost usage or repeated a call");
        Console.WriteLine("Gemini output checks passed (in-memory HTTP): actual SDK token cap, complete answers and streaming/non-streaming truncation rejection");
    }

    sealed class FixtureHandler : HttpMessageHandler
    {
        public bool Truncated { get; set; }
        public bool Structured { get; set; }
        public int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var config = body.RootElement.GetProperty("generationConfig");
            if (config.GetProperty("maxOutputTokens").GetInt32() != (Structured ? 8192 : 1024))
                throw new Exception("The SDK request lost the configured output cap");
            if (Structured) {
                if (config.GetProperty("responseMimeType").GetString() != "application/json") throw new Exception("Notes did not request structured JSON");
                var schema = config.TryGetProperty("responseJsonSchema", out var jsonSchema) ? jsonSchema : config.GetProperty("responseSchema");
                var ids = schema.GetProperty("properties").GetProperty("sections").GetProperty("items").GetProperty("properties").GetProperty("id").GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToArray();
                if (!ids.SequenceEqual(new[] { "new", "known-section" })) throw new Exception("The SDK section schema accepted an invented identity");
                if (schema.GetProperty("properties").GetProperty("sections").GetProperty("items").GetProperty("properties").TryGetProperty("markdown", out _))
                    throw new Exception("The provider contract still asks for a second authored copy of the same prose");
                if (schema.GetProperty("properties").GetProperty("sections").GetProperty("items").GetProperty("properties").TryGetProperty("baseVersion", out _))
                    throw new Exception("The provider contract asks a model to reproduce an already captured concurrency version");
            }
            var response = JsonSerializer.Serialize(new {
                candidates = new[] { new { content = new { role = "model", parts = new[] { new { text = "Complete answer" } } },
                    finishReason = Truncated ? "MAX_TOKENS" : "STOP" } },
                usageMetadata = new { promptTokenCount = 20, candidatesTokenCount = 10, totalTokenCount = 30 }
            });
            var stream = request.RequestUri!.AbsolutePath.Contains("streamGenerateContent");
            return new(HttpStatusCode.OK) { Content = new StringContent(stream ? "data: " + response + "\n\n" : response,
                Encoding.UTF8, stream ? "text/event-stream" : "application/json") };
        }
    }
}
