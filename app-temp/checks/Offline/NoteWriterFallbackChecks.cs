using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Playback.Api.Activity;
using Playback.Api.Notes;
using Playback.Api.Providers;

// The OpenRouter note writer retries transient failures and then lets Gemini write (decision 2026-10-19, option 2):
// which failures are retried, how long it waits, when it falls back, that other failures are reported as they are,
// and that the activity entry, live and saved, names the writer that actually answered. The live draft grows while
// OpenRouter streams. Every HTTP call is answered in memory and the Gemini writer is a fixture.
static class NoteWriterFallbackChecks
{
    const string Model = "qwen/qwen3.8-flash";
    static readonly OpenRouterTextModel.Limits Quick = new(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(300));
    static readonly string NoteInput = JsonSerializer.Serialize(new
    {
        pending = new[] { new { id = "T1" } }, materials = Array.Empty<object>(), sectionSources = Array.Empty<object>(),
        sectionMaterials = Array.Empty<object>(), editableSections = Array.Empty<object>(), citationSources = Array.Empty<object>()
    });

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
            Expect.That(NoteWriters.Create(new FakeGemini()) is FallbackNoteWriter { Provider: "openrouter", Model: Model },
                "The default OpenRouter note writer has no Gemini fallback");
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", oldKey);
            Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", oldOffline);
        }
        Console.WriteLine("Note writer fallback checks passed (in-memory HTTP): 503 and mid-stream errors retried after 1 s and 4 s, Retry-After honoured " +
            "and capped, Gemini writes after three transient failures or the retry window, other failures and cancellation not retried, " +
            "activity names the actual writer live and saved, streamed draft visible in activity before the reply ends");
    }

    // HTTP 503, then a provider error mid-stream, then a streamed answer: two waits, one reply, failures in usage.
    static async Task RetriedThenAnswered()
    {
        var replies = new Queue<Func<HttpResponseMessage>>([
            () => Status(HttpStatusCode.ServiceUnavailable),
            () => { var body = new SseBody(); body.Event(OpenRouterStub.Delta("{\"sections\":[")); body.Event(OpenRouterStub.ErrorChunk("server_error")); body.End(); return body.Response(); },
            () => OpenRouterStub.Stream([Reply("OpenRouter")])
        ]);
        var stub = new OpenRouterStub((_, _) => Task.FromResult(replies.Dequeue()()));
        var (writer, waits, gemini) = Writer(stub);
        var usage = new List<string>();
        var drafts = new List<string>();
        var patch = await writer.Generate("LectureSectionWriter", "Write notes.", NoteInput, CancellationToken.None, drafts.Add, usage.Add);
        Expect.That(patch.Sections.Single().Title == "OpenRouter" && stub.Calls == 3 && gemini.Calls == 0, "Two transient failures were not retried to an answer");
        Expect.That(waits.SequenceEqual([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4)]), "Retries did not wait 1 s and then 4 s");
        Expect.That(drafts.Contains("") && drafts[^1] == Reply("OpenRouter"), "The draft did not restart with the next attempt");
        var described = FallbackNoteWriter.Describe(usage[^1]);
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
            await writer.Generate("LectureSectionWriter", "Write notes.", NoteInput, CancellationToken.None);
            Expect.That(waits.SequenceEqual([TimeSpan.FromSeconds(expected)]), $"Retry-After {after} s was not waited as {expected} s");
        }
        // Past the retry window no further OpenRouter attempt starts; Gemini writes at once.
        var stub = new OpenRouterStub((_, _) => Task.FromResult(Status(HttpStatusCode.BadGateway)));
        var (late, lateWaits, gemini) = Writer(stub, window: TimeSpan.Zero);
        var patch = await late.Generate("LectureSectionWriter", "Write notes.", NoteInput, CancellationToken.None);
        Expect.That(stub.Calls == 1 && lateWaits.Count == 0 && gemini.Calls == 1 && patch.Sections.Single().Title == "Gemini",
            "A failure after the retry window did not go straight to Gemini");
    }

    static async Task FallsBackToGemini()
    {
        var stub = new OpenRouterStub((_, _) => Task.FromResult(Status(HttpStatusCode.ServiceUnavailable)));
        var (writer, waits, gemini) = Writer(stub);
        var usage = new List<string>();
        var drafts = new List<string>();
        var patch = await writer.Generate("LectureSectionWriter", "Write notes.", NoteInput, CancellationToken.None, drafts.Add, usage.Add);
        Expect.That(stub.Calls == 3 && waits.Count == 2 && gemini.Calls == 1 && patch.Sections.Single().Title == "Gemini",
            "Three transient OpenRouter failures did not fall back to Gemini");
        Expect.That(gemini.Names.Single() == "LectureSectionWriter" && gemini.Inputs.Single() == NoteInput,
            "Gemini did not receive the same task and input");
        Expect.That(drafts[^1] == Reply("Gemini"), "The Gemini draft was not shown");
        var described = FallbackNoteWriter.Describe(usage[^1]);
        Expect.That(usage[^1].Contains("\"InputTokenCount\"") && described is ("vertex", var model, var note) && model == gemini.Model &&
            note == $"Fallback: written by vertex/{gemini.Model} after openrouter/{Model} failed 3 times: OpenRouter text HTTP 503",
            "The fallback reply's usage did not name Gemini as the writer and why");

        // Without a Gemini key the OpenRouter failure is reported with its attempts.
        var (alone, _, _) = Writer(new OpenRouterStub((_, _) => Task.FromResult(Status(HttpStatusCode.ServiceUnavailable))), geminiConfigured: false);
        try { await alone.Generate("LectureSectionWriter", "Write notes.", NoteInput, CancellationToken.None); throw new CheckFailed("A failure without fallback was accepted"); }
        catch (InvalidOperationException ex) { Expect.That(ex.Message.Contains("HTTP 503") && ex.Message.Contains("3 OpenRouter attempts"), "The unfallen failure lost its cause"); }
    }

    // A refused key, an unreadable reply or the caller's cancellation is reported once, without retry or Gemini.
    static async Task FailuresNotRetried()
    {
        var stub = new OpenRouterStub((_, _) => Task.FromResult(Status(HttpStatusCode.Unauthorized)));
        var (writer, waits, gemini) = Writer(stub);
        try { await writer.Generate("LectureSectionWriter", "Write notes.", NoteInput, CancellationToken.None); throw new CheckFailed("A 401 was accepted"); }
        catch (HttpRequestException ex) when (ex is not OpenRouterTransientException) { }
        Expect.That(stub.Calls == 1 && waits.Count == 0 && gemini.Calls == 0, "An invalid key was retried or sent to Gemini");

        stub = new OpenRouterStub((_, _) => Task.FromResult(OpenRouterStub.Stream(["not json"])));
        (writer, waits, gemini) = Writer(stub);
        try { await writer.Generate("LectureSectionWriter", "Write notes.", NoteInput, CancellationToken.None); throw new CheckFailed("An unreadable reply was accepted"); }
        catch (JsonException) { }
        Expect.That(stub.Calls == 1 && gemini.Calls == 0, "An unreadable reply was retried or sent to Gemini");

        var body = new SseBody();
        stub = new OpenRouterStub((_, _) => Task.FromResult(body.Response()));
        (writer, waits, gemini) = Writer(stub);
        using var cancel = new CancellationTokenSource();
        var call = writer.Generate("LectureSectionWriter", "Write notes.", NoteInput, cancel.Token, _ => cancel.Cancel());
        body.Event(OpenRouterStub.Delta("{\"sections\":["));
        try { await call; throw new CheckFailed("A cancelled call completed"); }
        catch (OperationCanceledException) { }
        Expect.That(stub.Calls == 1 && gemini.Calls == 0, "A cancelled call was retried or sent to Gemini");
    }

    // Through the real workflow: the saved activity entry names Gemini, says why, and the note is saved; when Gemini
    // fails too, the failed entry still says a fallback was tried.
    static async Task WorkflowRecordsWriter()
    {
        foreach (var geminiFails in new[] { false, true })
        {
            const string id = "fallback";
            var store = new MemoryStore(); store.Add(id);
            var notes = new NoteStoreAdapter(store);
            var stub = new OpenRouterStub((_, _) => Task.FromResult(Status(HttpStatusCode.ServiceUnavailable)));
            var gemini = new FakeGemini { Fail = geminiFails };
            var writer = new FallbackNoteWriter(new OpenRouterNoteWriter(new OpenRouterTextModel(stub, Model, "none", Quick)), new GeminiNoteWriter(gemini),
                Policy([], TimeSpan.FromSeconds(60)));
            await using var workflow = new NoteWorkflow(notes, notes, writer, new FixtureNoteGate(),
                new NoteActivityAdapter(new AiActivity(store), NullLogger<NoteAgent>.Instance), new TestClock(), notes, notes, notes, notes);
            store.Speech(id, "a", "The lecturer demonstrates the kernel debugger.");
            try { await workflow.Generate(id, default); }
            catch (InvalidOperationException) when (geminiFails) { }
            var saved = store.Activity.Values.Single(x => x.Task == "Note revision");
            Expect.That(saved.Provider == "vertex" && saved.Model == gemini.Model && saved.UsageJson!.Contains("\"fallbackFrom\":\"openrouter/" + Model + "\"") &&
                saved.Summary!.Contains($"Fallback: written by vertex/{gemini.Model} after openrouter/{Model} failed 3 times: OpenRouter text HTTP 503"),
                $"The saved activity did not name the fallback writer ({saved.Provider}/{saved.Model}: {saved.Summary})");
            Expect.That(geminiFails ? saved.Status == "failed" && saved.Summary!.StartsWith("Synthetic Gemini outage")
                    : saved.Status == "completed" && store.Notes[id].Sections.Single().Title == "Gemini",
                "The fallback outcome was not saved as it happened");
        }
    }

    // The draft in the activity popover shows the streamed body while the provider is still sending it.
    static async Task LiveDraftWhileStreaming()
    {
        const string id = "streaming";
        var store = new MemoryStore(); store.Add(id);
        var notes = new NoteStoreAdapter(store);
        var activity = new AiActivity(store);
        var body = new SseBody();
        var alias = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stub = new OpenRouterStub(async (request, ct) =>
        {
            using var sent = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            alias.TrySetResult(Alias(sent.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!));
            return body.Response();
        });
        var writer = new FallbackNoteWriter(new OpenRouterNoteWriter(new OpenRouterTextModel(stub, Model, "none", Quick)), new GeminiNoteWriter(new FakeGemini()),
            Policy([], TimeSpan.FromSeconds(60)));
        await using var workflow = new NoteWorkflow(notes, notes, writer, new FixtureNoteGate(),
            new NoteActivityAdapter(activity, NullLogger<NoteAgent>.Instance), new TestClock(), notes, notes, notes, notes);
        store.Speech(id, "a", "The lecturer demonstrates the kernel debugger.");
        var generation = workflow.Generate(id, default);
        var reply = Placement("除錯器示範", "講者示範如何在核心除錯器中設定中斷點。", await alias.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        var cut = reply.IndexOf("如何", StringComparison.Ordinal);
        body.Comment();
        body.Event(OpenRouterStub.Delta(reply[..cut]));
        string? draft = null;
        for (var i = 0; i < 250 && draft?.Contains("講者示範") != true; i++)
        {
            await Task.Delay(20);
            draft = (await activity.Read(id)).SingleOrDefault(x => x.Task == "Note revision" && x.Status == "running")?.Draft;
        }
        Expect.That(draft == "講者示範" && !generation.IsCompleted, $"The activity draft did not show the streamed body before the reply ended ({draft})");
        body.Event(OpenRouterStub.Delta(reply[cut..]));
        body.Event(OpenRouterStub.Delta("", "stop"));
        body.Event(OpenRouterStub.UsageChunk());
        body.Done();
        await generation;
        var saved = store.Activity.Values.Single(x => x.Task == "Note revision");
        Expect.That(saved.Status == "completed" && saved.Provider == "openrouter" && saved.Model == Model && !saved.Summary!.Contains("Fallback") &&
            saved.UsageJson!.Contains("\"cached_tokens\":1000") && saved.Draft is null &&
            store.Notes[id].Sections.Single().Blocks.Single().Body.StartsWith("講者示範如何在核心除錯器中設定中斷點。"),
            "The streamed note was not saved with its usage and writer");
    }

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
        sections = new[] { new { id = "new", title, points = new[] { new { text = title + " wrote this.", sourceIds = new[] { "T1" }, retains = Array.Empty<string>() } } } },
        deferred = Array.Empty<object>()
    });

    static string Placement(string title, string text, string source) => JsonSerializer.Serialize(new
    {
        outline = new[] { new { key = "new1", title, parent = "" } },
        blocks = new[] { new { topic = "new1", role = "detail", body = text, sourceIds = new[] { source } } },
        operations = Array.Empty<object>(), deferred = Array.Empty<object>()
    }, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    // The input names each source by an alias in its lecture rows [alias, source, start, text, uncertain].
    static string Alias(string input)
    {
        using var document = JsonDocument.Parse(input);
        return document.RootElement.GetProperty("lecture").EnumerateArray().First().EnumerateArray().First().GetString()!;
    }

    // Gemini as a fixture: answers note sections, or a placement for workflow input, or fails like an outage.
    sealed class FakeGemini : GeminiLanguageModel
    {
        public bool Configured = true, Fail;
        public int Calls;
        public readonly List<string> Names = [], Inputs = [];
        public override bool IsConfigured => Configured;
        public override Task<string> Generate(string name, string instructions, string prompt, CancellationToken ct, string? model = null,
            Action<string>? onUpdate = null, Action<string>? onUsage = null)
        {
            Calls++;
            Names.Add(name);
            Inputs.Add(prompt);
            onUsage?.Invoke("{\"InputTokenCount\":900,\"OutputTokenCount\":60}");
            if (Fail) throw new InvalidOperationException("Synthetic Gemini outage");
            var reply = prompt.Contains("\"lecture\"") ? Placement("Gemini", "Gemini wrote this.", Alias(prompt)) : Reply("Gemini");
            onUpdate?.Invoke(reply);
            return Task.FromResult(reply);
        }
    }
}
