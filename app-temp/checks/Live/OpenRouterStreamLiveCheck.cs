using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Playback.Api.Activity;
using Playback.Api.Notes;
using Playback.Api.Providers;

// Paid, small (at most three requests): two streamed Qwen3.8 Flash calls through OpenRouter on a three-sentence synthetic lecture (no audio):
// one note revision through the production NoteWorkflow (structured output + streaming + activity draft) and one
// plain streamed answer. It records what the SSE stream actually contained (comments, events, where usage and the
// finish reason came, [DONE]) and when the first draft appeared. Needs PLAYBACK_OPENROUTER_STREAM_LIVE=yes (set by
// run-openrouter-stream-live.mjs) and OPENROUTER_API_KEY in the process environment.
static class OpenRouterStreamLiveCheck
{
    const string Model = "qwen/qwen3.8-flash";

    public static async Task Run()
    {
        if (Environment.GetEnvironmentVariable("PLAYBACK_OPENROUTER_STREAM_LIVE") != "yes")
            throw new InvalidOperationException("Set PLAYBACK_OPENROUTER_STREAM_LIVE=yes to allow two small OpenRouter calls");
        if (OpenRouterKey.Read() is null) throw new InvalidOperationException("Set a real OPENROUTER_API_KEY in this process environment");
        Environment.SetEnvironmentVariable("PLAYBACK_OFFLINE_TEST", "no");
        Environment.SetEnvironmentVariable("PLAYBACK_AUTO_NOTES", "no");

        // 1. A note revision through the workflow, as live notes run it.
        const string id = "openrouter-stream-live";
        var store = new MemoryStore(); store.Add(id);
        var notes = new NoteStoreAdapter(store);
        var activity = new AiActivity(store);
        var recorder = new RecordingHandler();
        using var model = new OpenRouterTextModel(recorder, Model, "none");
        // At most two OpenRouter attempts and never Gemini, so the run stays within its three-call budget.
        var writer = new FallbackNoteWriter(new OpenRouterNoteWriter(model), new GeminiNoteWriter(new NoGemini()),
            FallbackNoteWriter.Default with { Attempts = 2 });
        await using (var workflow = new NoteWorkflow(notes, notes, writer, new FixtureNoteGate(),
            new NoteActivityAdapter(activity, NullLogger<NoteAgent>.Instance), new TestClock(), notes, notes, notes, notes))
        {
            store.Speech(id, "a", "Synthetic lecture. A kernel debugger can stop the whole operating system at a breakpoint.");
            store.Speech(id, "b", "Local kernel debugging only reads memory; it cannot set breakpoints.");
            store.Speech(id, "c", "Symbols come from the Microsoft symbol server and are cached on the local disk.");
            var watch = Stopwatch.StartNew();
            long? firstDraftMs = null;
            var drafts = 0;
            string? lastDraft = null;
            var generation = workflow.Generate(id, default);
            while (!generation.IsCompleted)
            {
                await Task.Delay(50);
                var draft = (await activity.Read(id)).SingleOrDefault(x => x.Task == "Note revision" && x.Status == "running")?.Draft;
                if (!string.IsNullOrEmpty(draft) && draft != lastDraft) { drafts++; lastDraft = draft; firstDraftMs ??= watch.ElapsedMilliseconds; }
            }
            await generation;
            var saved = store.Activity.Values.Single(x => x.Task == "Note revision");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                call = "note revision (structured output, streamed)",
                status = saved.Status, saved.Provider, saved.Model, saved.Summary, totalMs = watch.ElapsedMilliseconds, firstDraftMs,
                distinctDraftsSeen = drafts, usage = saved.UsageJson, stream = recorder.Describe(),
                note = store.Notes[id].Sections.Select(x => new { x.Title, blocks = x.Blocks.Select(b => b.Body) })
            }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            Expect.That(saved.Status == "completed" && saved.Provider == "openrouter" && firstDraftMs < watch.ElapsedMilliseconds,
                "The live note revision did not stream a draft before it completed through OpenRouter");
        }

        // 2. A plain streamed answer without a schema.
        var plain = new RecordingHandler();
        using var routine = new OpenRouterTextModel(plain, Model, "none");
        var usage = "";
        var updates = 0;
        var answer = await routine.Generate("PlaybackQuestionAnswerer", "Answer in one short English sentence.",
            "What does a kernel debugger breakpoint do?", CancellationToken.None, x => usage = x, _ => updates++);
        Console.WriteLine(JsonSerializer.Serialize(new { call = "plain answer (streamed)", answer, updates, usage, stream = plain.Describe() },
            new JsonSerializerOptions { WriteIndented = true }));
    }

    sealed class NoGemini : GeminiLanguageModel { public override bool IsConfigured => false; }

    // Keeps a copy of each streamed response body (bounded) with the time each piece arrived, to describe the stream.
    sealed class RecordingHandler() : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        readonly List<(long Ms, string Text)> pieces = [];
        readonly Stopwatch watch = new();
        string? contentType;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            watch.Restart();
            var response = await base.SendAsync(request, ct);
            contentType = response.Content.Headers.ContentType?.MediaType;
            var inner = await response.Content.ReadAsStreamAsync(ct);
            var content = new StreamContent(new Tee(inner, this));
            foreach (var header in response.Content.Headers) content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content = content;
            return response;
        }

        void Add(byte[] bytes, int count)
        {
            lock (pieces) if (pieces.Sum(x => x.Text.Length) < 1_000_000) pieces.Add((watch.ElapsedMilliseconds, Encoding.UTF8.GetString(bytes, 0, count)));
        }

        public object Describe()
        {
            var text = string.Concat(pieces.Select(x => x.Text));
            var lines = text.Split('\n');
            var events = lines.Where(x => x.StartsWith("data:")).Select(x => x[5..].Trim()).ToList();
            var json = events.Where(x => x != "[DONE]").Select(x => JsonNode.Parse(x)).ToList();
            var usageEvents = json.Select((x, i) => (x, i)).Where(x => x.x?["usage"] is JsonObject).ToList();
            var finishes = json.Select(x => x?["choices"]?[0]?["finish_reason"]?.ToString()).Where(x => x is not null).ToList();
            var commentTimes = new List<long>();
            foreach (var (ms, piece) in pieces) if (piece.Contains(": OPENROUTER")) commentTimes.Add(ms);
            return new
            {
                contentType,
                comments = lines.Count(x => x.StartsWith(':')),
                commentSample = lines.FirstOrDefault(x => x.StartsWith(':')),
                commentArrivalMs = commentTimes.Take(10),
                firstByteMs = pieces.FirstOrDefault().Ms,
                firstContentMs = pieces.FirstOrDefault(x => x.Text.Contains("\"content\":\"") && !x.Text.Contains("\"content\":\"\"")).Ms,
                lastByteMs = pieces.LastOrDefault().Ms,
                dataEvents = events.Count,
                endsWithDone = events.LastOrDefault() == "[DONE]",
                usageEventIndex = usageEvents.Select(x => x.i),
                usageEventIsLastJson = usageEvents.Count == 1 && usageEvents[0].i == json.Count - 1,
                usageEventChoices = usageEvents.Select(x => x.x?["choices"]?.ToJsonString()),
                finishReasons = finishes,
                errorEvents = json.Count(x => x?["error"] is not null),
                providers = json.Select(x => x?["provider"]?.ToString()).Distinct()
            };
        }

        sealed class Tee(Stream inner, RecordingHandler owner) : Stream
        {
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            {
                var read = await inner.ReadAsync(buffer, ct);
                if (read > 0) owner.Add(buffer[..read].ToArray(), read);
                return read;
            }
            public override int Read(byte[] buffer, int offset, int count)
            {
                var read = inner.Read(buffer, offset, count);
                if (read > 0) owner.Add(buffer[offset..(offset + read)], read);
                return read;
            }
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        }
    }
}
