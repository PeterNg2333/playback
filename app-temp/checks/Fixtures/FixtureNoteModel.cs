using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Playback.Api.Activity;
using Playback.Api.Db;
using Playback.Api.Notes;
using Playback.Api.Providers;
using Playback.Application.Notes;

// A note model that answers each three-speed task from its input, so note checks can follow every source without a provider:
// a jot or segment block per transcript line (citing it), one outline topic per segment title, and a polish that keeps the
// notes as they are. Switches make it fail or drop citations on purpose.
sealed class FixtureNoteModel : GeminiLanguageModel
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public override bool IsConfigured => true;
    public readonly ConcurrentDictionary<string, int> Calls = new();
    public int TotalCalls => Calls.Values.Sum();
    public bool FailNext, PolishDropsCitations;
    public readonly ConcurrentBag<string> Inputs = new();

    public override Task<string> Complete(NoteTask task, string instructions, string prompt, CancellationToken ct,
        Action<string>? onUpdate = null, Action<string>? onUsage = null)
    {
        Calls.AddOrUpdate(task.Name, 1, (_, n) => n + 1);
        Inputs.Add(prompt);
        if (FailNext)
        {
            FailNext = false;
            throw new TimeoutException("Synthetic generation timeout");
        }
        // The input is the course prefix line, then the task's JSON.
        using var doc = JsonDocument.Parse(prompt[(prompt.IndexOf('\n') + 1)..]);
        var root = doc.RootElement;
        static IEnumerable<JsonElement> Rows(JsonElement value) => value.EnumerateArray();
        object reply = task.Name switch
        {
            "NoteLiveJot" => new { points = Rows(root.GetProperty("new")).Select(r => new { text = r[3].GetString() + " [" + r[0].GetString() + "]", sources = new[] { r[0].GetString() } }) },
            "NoteSegment" => new
            {
                title = "Window " + Rows(root.GetProperty("window")).First()[1].GetString(),
                blocks = Rows(root.GetProperty("window")).Select(r => new { kind = "concept", markdown = r[3].GetString() + " [" + r[0].GetString() + "]", sources = new[] { r[0].GetString() } }),
                concepts = Rows(root.GetProperty("window")).Take(1).Select(r => new { name = r[3].GetString()!.Split(' ')[0], definition = "Fixture concept", sources = new[] { r[0].GetString() } }),
                corrections = Array.Empty<object>()
            },
            "NoteOutline" => new
            {
                topics = Rows(root.GetProperty("blocks")).GroupBy(b => b[2].GetString()).Select((g, i) => new { key = "k" + (i + 1), title = g.Key, parent = "", blocks = g.Select(b => b[0].GetString()) }),
                summary = new[] { "Fixture summary" },
                reviewQuestions = new[] { "Fixture question?" }
            },
            "NotePolish" => new
            {
                topics = Rows(root.GetProperty("topics")).Select(t => new { key = t.GetProperty("key").GetString(),
                    markdown = PolishDropsCitations ? System.Text.RegularExpressions.Regex.Replace(t.GetProperty("notes").GetString()!, @"\s*\[T\d+(?:, T\d+)*\]", "") + " polished"
                        : t.GetProperty("notes").GetString() + "\n\nPolished." })
            },
            "NoteRewrite" => new { markdown = "Rewritten: " + root.GetProperty("target").GetProperty("markdown").GetString() },
            _ => throw new InvalidOperationException("Unknown note task " + task.Name)
        };
        var text = JsonSerializer.Serialize(reply, Json);
        onUsage?.Invoke("{\"input_tokens\":20,\"output_tokens\":12}");
        onUpdate?.Invoke(text);
        return Task.FromResult(text);
    }

    // The production workflow over an in-memory store, written by this model.
    public NoteWorkflow Workflow(PlaybackStore store) => new(new NoteStoreAdapter(store), new NoteStoreAdapter(store), new GeminiNoteWriter(this),
        new NoteActivityAdapter(new AiActivity(store), NullLogger<NoteAgent>.Instance), new OpenccNoteScript());
}
