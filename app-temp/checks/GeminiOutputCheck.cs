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
        using var chat = new OutputGuardChatClient(provider.AsIChatClient(GeminiLanguageModel.DefaultModel));
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
        Console.WriteLine("Gemini output checks passed (in-memory HTTP): actual SDK token cap, complete answers and streaming/non-streaming truncation rejection");
    }

    sealed class FixtureHandler : HttpMessageHandler
    {
        public bool Truncated { get; set; }
        public int Requests { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            if (body.RootElement.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32() != 1024)
                throw new Exception("The SDK request lost the configured output cap");
            var response = JsonSerializer.Serialize(new {
                candidates = new[] { new { content = new { role = "model", parts = new[] { new { text = "Complete answer" } } },
                    finishReason = Truncated ? "MAX_TOKENS" : "STOP" } }
            });
            var stream = request.RequestUri!.AbsolutePath.Contains("streamGenerateContent");
            return new(HttpStatusCode.OK) { Content = new StringContent(stream ? "data: " + response + "\n\n" : response,
                Encoding.UTF8, stream ? "text/event-stream" : "application/json") };
        }
    }
}
