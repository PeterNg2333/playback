using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Playback.Application.Activity;
using Playback.Application.Notes;
using Playback.Infrastructure.Providers;

// The OpenRouter note writer retries transient failures and then lets Gemini write (decision 2026-10-19, option 2):
// which failures are retried, how long it waits, when it falls back, that other failures are reported as they are,
// and that the activity entry, live and saved, names the writer that actually answered. The live draft grows while
// OpenRouter streams. Every HTTP call is answered in memory and the Gemini writer is a fixture.
static class NoteWriterFallbackChecks
{
    const string Model = "qwen/qwen3.8-flash";
    static readonly OpenRouterTextModel.Limits Quick = new(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(300));
    static readonly NoteTask Segment = NotePrompts.Segment;
    const string NoteInput = "{\"course\":{\"materials\":[]}}\n{\"window\":[[\"T1\",\"0:00\",\"mic\",\"The lecturer demonstrates the kernel debugger.\"]]}";
    static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static async Task Run()
    {
        var oldKey = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");
        var oldOffline = Environment.GetEnvironmentVariable("PLAYBACK_OFFLINE_TEST");
        try
        {
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "fixture-key");
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", null);
            await RetriedThenAnswered();
            await RetryWaits();
            await FallsBackToGemini();
            await FailuresNotRetried();
            await WorkflowRecordsWriter();
            await LiveDraftWhileStreaming();
            await ThreeLayers();
            Expect.That(NoteWriters.Create(new FakeGemini()) is FallbackNoteWriter { Provider: "openrouter", Model: Model, Models: [Model, "z-ai/glm-5.3-flash"] },
                "The default OpenRouter note writer is not Qwen, then GLM, then Gemini");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", oldKey);
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", oldOffline);
        }
        Console.WriteLine("Note writer fallback checks passed (in-memory HTTP): 503 and mid-stream errors retried after 1 s and 4 s, Retry-After honoured " +
            "and capped, Gemini writes after three transient failures or the retry window, other failures and cancellation not retried, " +
            "activity names the actual writer live and saved, streamed draft visible in activity before the reply ends; " +
            "Qwen (reasoning none) → GLM (reasoning low) → Gemini in order, with the answering model and serving provider in the activity");
    }

    // HTTP 503, then a provider error mid-stream, then a streamed answer: two waits, one reply, failures in usage.
    static async Task RetriedThenAnswered()
    {
        var replies = new Queue<Func<HttpResponseMessage>>([
            () => Status(HttpStatusCode.ServiceUnavailable),
            () => { var body = new SseBody(); body.Event(OpenRouterStub.Delta("{\"title\":")); body.Event(OpenRouterStub.ErrorChunk("server_error")); body.End(); return body.Response(); },
            () => OpenRouterStub.Stream([Reply("OpenRouter")])
        ]);
        var stub = new OpenRouterStub((_, _) => Task.FromResult(replies.Dequeue()()));
        var (writer, waits, gemini) = Writer(stub);
        var usage = new List<string>();
        var drafts = new List<string>();
        var reply = await writer.Complete(Segment, "Write notes.", NoteInput, CancellationToken.None, drafts.Add, usage.Add);
        Expect.That(Title(reply) == "OpenRouter" && stub.Calls == 3 && gemini.Calls == 0, "Two transient failures were not retried to an answer");
        Expect.That(waits.SequenceEqual([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4)]), "Retries did not wait 1 s and then 4 s");
        Expect.That(drafts.Contains("") && drafts[^1] == Reply("OpenRouter"), "The draft did not restart with the next attempt");
        var described = NoteWriterReport.Describe(usage[^1], Model);
        Expect.That(usage[^1].Contains("\"cost\"") && described is ("openrouter", Model, var note) &&
            note.Contains("2 failed attempts") && note.Contains("HTTP 503") && note.Contains("server_error"),
            "The answered call's usage did not keep its cost and the failed attempts");
    }

    static async Task RetryWaits()
    {
        // The provider's Retry-After is honoured, and capped at 20 s.
        foreach (var (after, expected) in new[] { (7, 7), (90, 20) })
        {
            var replies = new Queue<Func<HttpResponseMessage>>([
                () => { var r = Status(HttpStatusCode.TooManyRequests); r.Headers.RetryAfter = new(TimeSpan.FromSeconds(after)); return r; },
                () => OpenRouterStub.Stream([Reply("OpenRouter")])
            ]);
            var (writer, waits, _) = Writer(new OpenRouterStub((_, _) => Task.FromResult(replies.Dequeue()())));
            await writer.Complete(Segment, "Write notes.", NoteInput, CancellationToken.None);
            Expect.That(waits.SequenceEqual([TimeSpan.FromSeconds(expected)]), $"Retry-After {after} s was not waited as {expected} s");
        }
        // Past the retry window no further OpenRouter attempt starts; Gemini writes at once.
        var stub = new OpenRouterStub((_, _) => Task.FromResult(Status(HttpStatusCode.BadGateway)));
        var (late, lateWaits, gemini) = Writer(stub, window: TimeSpan.Zero);
        var reply = await late.Complete(Segment, "Write notes.", NoteInput, CancellationToken.None);
        Expect.That(stub.Calls == 1 && lateWaits.Count == 0 && gemini.Calls == 1 && Title(reply) == "Gemini",
            "A failure after the retry window did not go straight to Gemini");
    }

    static async Task FallsBackToGemini()
    {
        var stub = new OpenRouterStub((_, _) => Task.FromResult(Status(HttpStatusCode.ServiceUnavailable)));
        var (writer, waits, gemini) = Writer(stub);
        var usage = new List<string>();
        var drafts = new List<string>();
        var reply = await writer.Complete(Segment, "Write notes.", NoteInput, CancellationToken.None, drafts.Add, usage.Add);
        Expect.That(stub.Calls == 3 && waits.Count == 2 && gemini.Calls == 1 && Title(reply) == "Gemini",
            "Three transient OpenRouter failures did not fall back to Gemini");
        Expect.That(gemini.Names.Single() == Segment.Name && gemini.Inputs.Single() == NoteInput, "Gemini did not receive the same task and input");
        Expect.That(drafts[^1] == Reply("Gemini"), "The Gemini draft was not shown");
        var described = NoteWriterReport.Describe(usage[^1], Model);
        Expect.That(usage[^1].Contains("\"InputTokenCount\"") && described is ("vertex", var model, var note) && model == gemini.Model &&
            note == $"Fallback: written by vertex/{gemini.Model} after openrouter/{Model} failed 3 times: OpenRouter text HTTP 503",
            "The fallback reply's usage did not name Gemini as the writer and why");

        // Without a Gemini key the OpenRouter failure is reported with its attempts.
        var (alone, _, _) = Writer(new OpenRouterStub((_, _) => Task.FromResult(Status(HttpStatusCode.ServiceUnavailable))), geminiConfigured: false);
        try { await alone.Complete(Segment, "Write notes.", NoteInput, CancellationToken.None); throw new CheckFailed("A failure without fallback was accepted"); }
        catch (InvalidOperationException ex) { Expect.That(ex.Message.Contains("HTTP 503") && ex.Message.Contains("3 OpenRouter attempts"), "The unfallen failure lost its cause"); }
    }

    // A refused key or the caller's cancellation is reported once, without retry or Gemini. An unreadable reply is returned
    // once, as written: the workflow rejects it when it parses the task's JSON.
    static async Task FailuresNotRetried()
    {
        var stub = new OpenRouterStub((_, _) => Task.FromResult(Status(HttpStatusCode.Unauthorized)));
        var (writer, waits, gemini) = Writer(stub);
        try { await writer.Complete(Segment, "Write notes.", NoteInput, CancellationToken.None); throw new CheckFailed("A 401 was accepted"); }
        catch (HttpRequestException ex) when (ex is not OpenRouterTransientException) { }
        Expect.That(stub.Calls == 1 && waits.Count == 0 && gemini.Calls == 0, "An invalid key was retried or sent to Gemini");

        stub = new OpenRouterStub((_, _) => Task.FromResult(OpenRouterStub.Stream(["not json"])));
        (writer, waits, gemini) = Writer(stub);
        var text = await writer.Complete(Segment, "Write notes.", NoteInput, CancellationToken.None);
        Expect.That(text == "not json" && stub.Calls == 1 && gemini.Calls == 0, "An unreadable reply was retried, sent to Gemini or changed");

        var body = new SseBody();
        stub = new OpenRouterStub((_, _) => Task.FromResult(body.Response()));
        (writer, waits, gemini) = Writer(stub);
        using var cancel = new CancellationTokenSource();
        var call = writer.Complete(Segment, "Write notes.", NoteInput, cancel.Token, _ => cancel.Cancel());
        body.Event(OpenRouterStub.Delta("{\"title\":"));
        try { await call; throw new CheckFailed("A cancelled call completed"); }
        catch (OperationCanceledException) { }
        Expect.That(stub.Calls == 1 && gemini.Calls == 0, "A cancelled call was retried or sent to Gemini");
    }

    // Through the real workflow: the saved segment activity names Gemini, says why, and the notes are saved; when Gemini
    // fails too, the failed entry still says a fallback was tried.
    static async Task WorkflowRecordsWriter()
    {
        foreach (var geminiFails in new[] { false, true })
        {
            const string id = "fallback";
            var store = new MemoryStore(); store.Add(id);
            var stub = new OpenRouterStub((_, _) => Task.FromResult(Status(HttpStatusCode.ServiceUnavailable)));
            var gemini = new FakeGemini { Fail = geminiFails };
            var writer = new FallbackNoteWriter(new OpenRouterNoteWriter(new OpenRouterTextModel(stub, Model, "none", Quick)), new GeminiNoteWriter(gemini),
                Policy([], TimeSpan.FromSeconds(60)));
            await using var workflow = Workflow(store, writer);
            store.Speech(id, "a", "The lecturer demonstrates the kernel debugger.");
            try { await workflow.Generate(id, default); }
            catch (InvalidOperationException) when (geminiFails) { }
            var saved = store.Activity.Values.Single(x => x.Task == "Note segment");
            Expect.That(saved.Provider == "vertex" && saved.Model == gemini.Model && saved.UsageJson!.Contains("\"fallbackFrom\":\"openrouter/" + Model + "\"") &&
                saved.Summary!.Contains($"Fallback: written by vertex/{gemini.Model} after openrouter/{Model} failed 3 times: OpenRouter text HTTP 503"),
                $"The saved activity did not name the fallback writer ({saved.Provider}/{saved.Model}: {saved.Summary})");
            Expect.That(geminiFails ? saved.Status == "failed" && saved.Summary!.StartsWith("Synthetic Gemini outage")
                    : saved.Status == "completed" && store.Notes[id].Sections.Single().Title == "Gemini",
                "The fallback outcome was not saved as it happened");
        }
    }

    // Three layers in order, through the real workflow and the writer NoteWriters builds from its fallback list: Qwen
    // (reasoning none), then GLM (reasoning low) at once when Qwen is rate-limited, then Gemini after the attempts. The
    // segment's activity entry names whichever answered and which provider served it. The outline and polish answer at once.
    static async Task ThreeLayers()
    {
        foreach (var layer in new[] { "qwen", "glm", "gemini" })
        {
            const string id = "layers";
            var store = new MemoryStore(); store.Add(id);
            var sentModels = new List<string>();
            var stub = new OpenRouterStub(async (request, ct) =>
            {
                using var sent = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var model = sent.RootElement.GetProperty("model").GetString()!;
                var reply = Answer(sent.RootElement, "除錯器示範", "講者示範如何設定中斷點。");
                if (TaskName(sent.RootElement) != Segment.Name) return OpenRouterStub.Stream([reply]);
                sentModels.Add(model + "@" + sent.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
                // Alibaba's shared pool is rate-limited for Qwen in the "glm" and "gemini" layers, GLM's providers too in "gemini".
                return (layer, model) switch
                {
                    ("qwen", Model) => OpenRouterStub.Stream([reply], model: "qwen/qwen3.8-flash-20260826"),
                    ("glm", "z-ai/glm-5.3-flash") => OpenRouterStub.Stream([reply], model: "z-ai/glm-5.3-flash", provider: "Fireworks"),
                    _ => Status(HttpStatusCode.TooManyRequests)
                };
            });
            var gemini = new FakeGemini();
            var openRouter = NoteWriters.ModelFallbacks[Model].Select(x => new OpenRouterNoteWriter(new OpenRouterTextModel(stub, x.Model, x.Reasoning, Quick)))
                .Prepend(new OpenRouterNoteWriter(new OpenRouterTextModel(stub, Model, "none", Quick))).ToList();
            var waits = new List<TimeSpan>();
            var writer = new FallbackNoteWriter(openRouter, new GeminiNoteWriter(gemini), Policy(waits, TimeSpan.FromSeconds(60)));
            await using var workflow = Workflow(store, writer);
            store.Speech(id, "a", "The lecturer demonstrates the kernel debugger.");
            await workflow.Generate(id, default);
            var saved = store.Activity.Values.Single(x => x.Task == "Note segment");
            var (provider, model, summary, requests) = layer switch
            {
                "qwen" => ("openrouter", Model, " · Served by Alibaba", new[] { "qwen/qwen3.8-flash@none" }),
                "glm" => ("openrouter", "z-ai/glm-5.3-flash", $" · Fallback: written by openrouter/z-ai/glm-5.3-flash after openrouter/{Model} failed 1 time: " +
                    "OpenRouter text HTTP 429 · Served by Fireworks", ["qwen/qwen3.8-flash@none", "z-ai/glm-5.3-flash@low"]),
                _ => ("vertex", gemini.Model, $" · Fallback: written by vertex/{gemini.Model} after openrouter/{Model} failed 3 times: OpenRouter text HTTP 429",
                    ["qwen/qwen3.8-flash@none", "z-ai/glm-5.3-flash@low", "qwen/qwen3.8-flash@none"])
            };
            Expect.That(saved.Status == "completed" && saved.Provider == provider && saved.Model == model && saved.Summary!.EndsWith(summary) &&
                sentModels.SequenceEqual(requests) && gemini.Calls == (layer == "gemini" ? 1 : 0) &&
                waits.SequenceEqual(layer == "gemini" ? [TimeSpan.FromSeconds(1)] : []),
                $"The {layer} layer was not reached in order or not shown in the activity ({string.Join(", ", sentModels)}; {saved.Provider}/{saved.Model}: {saved.Summary})");
            if (layer == "glm")
                Expect.That(saved.UsageJson!.Contains("\"failures\":[{\"model\":\"qwen/qwen3.8-flash\",\"error\":\"OpenRouter text HTTP 429\"") &&
                    saved.UsageJson.Contains("\"provider\":\"Fireworks\""), "The usage did not keep Qwen's failure and GLM's serving provider");
        }
    }

    // The draft in the activity popover shows the streamed segment while the provider is still sending it.
    static async Task LiveDraftWhileStreaming()
    {
        const string id = "streaming";
        var store = new MemoryStore(); store.Add(id);
        var activity = new AiActivity(store);
        var body = new SseBody();
        var segmentRequest = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stub = new OpenRouterStub(async (request, ct) =>
        {
            var text = await request.Content!.ReadAsStringAsync(ct);
            using var sent = JsonDocument.Parse(text);
            if (TaskName(sent.RootElement) != Segment.Name) return OpenRouterStub.Stream([Answer(sent.RootElement, "除錯器示範", "")]);
            segmentRequest.TrySetResult(text);
            return body.Response();
        });
        var writer = new FallbackNoteWriter(new OpenRouterNoteWriter(new OpenRouterTextModel(stub, Model, "none", Quick)), new GeminiNoteWriter(new FakeGemini()),
            Policy([], TimeSpan.FromSeconds(60)));
        await using var workflow = new NoteWorkflow(new NoteStoreAdapter(store), new NoteStoreAdapter(store), writer,
            new NoteActivityAdapter(activity, NullLogger<NoteAgent>.Instance), null, new TestClock());
        store.Speech(id, "a", "The lecturer demonstrates the kernel debugger.");
        var generation = workflow.Generate(id, default);
        using var sent = JsonDocument.Parse(await segmentRequest.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        var reply = Answer(sent.RootElement, "除錯器示範", "講者示範如何在核心除錯器中設定中斷點。");
        var cut = reply.IndexOf("如何", StringComparison.Ordinal);
        body.Comment();
        body.Event(OpenRouterStub.Delta(reply[..cut]));
        string? draft = null;
        for (var i = 0; i < 250 && draft?.Contains("講者示範") != true; i++)
        {
            await Task.Delay(20);
            draft = (await activity.Read(id)).SingleOrDefault(x => x.Task == "Note segment" && x.Status == "running")?.Draft;
        }
        Expect.That(draft?.EndsWith("講者示範") == true && !generation.IsCompleted, $"The activity draft did not show the streamed body before the reply ended ({draft})");
        body.Event(OpenRouterStub.Delta(reply[cut..]));
        body.Event(OpenRouterStub.Delta("", "stop"));
        body.Event(OpenRouterStub.UsageChunk());
        body.Done();
        await generation;
        var saved = store.Activity.Values.Single(x => x.Task == "Note segment");
        Expect.That(saved.Status == "completed" && saved.Provider == "openrouter" && saved.Model == Model && !saved.Summary!.Contains("Fallback") && saved.Summary.EndsWith(" · Served by Alibaba") &&
            saved.UsageJson!.Contains("\"cached_tokens\":1000") && saved.Draft is null &&
            store.Notes[id].Segments.Single().Blocks.Single().Body.StartsWith("講者示範如何在核心除錯器中設定中斷點。"),
            "The streamed segment was not saved with its usage and writer");
    }

    static NoteWorkflow Workflow(MemoryStore store, INoteWriter writer) => new(new NoteStoreAdapter(store), new NoteStoreAdapter(store), writer,
        new NoteActivityAdapter(new AiActivity(store), NullLogger<NoteAgent>.Instance), null, new TestClock());

    static (FallbackNoteWriter Writer, List<TimeSpan> Waits, FakeGemini Gemini) Writer(OpenRouterStub stub, TimeSpan? window = null, bool geminiConfigured = true)
    {
        var waits = new List<TimeSpan>();
        var gemini = new FakeGemini { Configured = geminiConfigured };
        var writer = new FallbackNoteWriter(new OpenRouterNoteWriter(new OpenRouterTextModel(stub, Model, "none", Quick)), new GeminiNoteWriter(gemini),
            Policy(waits, window ?? TimeSpan.FromSeconds(60)));
        return (writer, waits, gemini);
    }

    // The default attempts and waits, with waiting recorded instead of slept.
    static FallbackNoteWriter.Policy Policy(List<TimeSpan> waits, TimeSpan window) => FallbackNoteWriter.Default with
    {
        Window = window,
        Wait = (wait, _) => { waits.Add(wait); return Task.CompletedTask; }
    };

    static HttpResponseMessage Status(HttpStatusCode status) => new(status) { Content = new StringContent("quoted lecture text") };

    static string Reply(string title) => JsonSerializer.Serialize(new
    {
        title, blocks = new[] { new { kind = "concept", markdown = title + " wrote this. [T1]", sources = new[] { "T1" } } },
        concepts = Array.Empty<object>(), corrections = Array.Empty<object>()
    }, Json);

    static string Title(string reply) => JsonDocument.Parse(reply).RootElement.GetProperty("title").GetString()!;
    static string TaskName(JsonElement request) => request.GetProperty("response_format").GetProperty("json_schema").GetProperty("name").GetString()!;

    // A reply in the shape the task asks for: a segment of one block per transcript line, an outline of one topic, a polish
    // that keeps the notes.
    static string Answer(JsonElement request, string title, string text) => Answer(TaskName(request),
        request.GetProperty("messages")[1].GetProperty("content").GetString()!, title, text);
    static string Answer(string task, string input, string title, string text)
    {
        using var document = JsonDocument.Parse(input[(input.IndexOf('\n') + 1)..]);
        var root = document.RootElement;
        object reply = task switch
        {
            "NoteSegment" => new { title, blocks = root.GetProperty("window").EnumerateArray().Select(r => new { kind = "concept",
                markdown = (text.Length > 0 ? text : r[3].GetString()) + " [" + r[0].GetString() + "]", sources = new[] { r[0].GetString() } }),
                concepts = Array.Empty<object>(), corrections = Array.Empty<object>() },
            "NoteOutline" => new { topics = new[] { new { key = "k1", title, parent = "",
                blocks = root.GetProperty("blocks").EnumerateArray().Select(b => b[0].GetString()) } },
                summary = Array.Empty<string>(), reviewQuestions = Array.Empty<string>() },
            "NotePolish" => new { topics = root.GetProperty("topics").EnumerateArray().Select(t => new { key = t.GetProperty("key").GetString(),
                markdown = t.GetProperty("notes").GetString() }) },
            _ => throw new InvalidOperationException("Unexpected note task " + task)
        };
        return JsonSerializer.Serialize(reply, Json);
    }

    // Gemini as a fixture: answers each note task, or fails like an outage.
    sealed class FakeGemini : GeminiLanguageModel
    {
        public bool Configured = true, Fail;
        public int Calls;
        public readonly List<string> Names = [], Inputs = [];
        public override bool IsConfigured => Configured;
        public override Task<string> Complete(NoteTask task, string instructions, string prompt, CancellationToken ct,
            Action<string>? onUpdate = null, Action<string>? onUsage = null)
        {
            Calls++;
            Names.Add(task.Name);
            Inputs.Add(prompt);
            onUsage?.Invoke("{\"InputTokenCount\":900,\"OutputTokenCount\":60}");
            if (Fail) throw new InvalidOperationException("Synthetic Gemini outage");
            var reply = prompt == NoteInput ? Reply("Gemini") : Answer(task.Name, prompt, "Gemini", "Gemini wrote this.");
            onUpdate?.Invoke(reply);
            return Task.FromResult(reply);
        }
    }
}
