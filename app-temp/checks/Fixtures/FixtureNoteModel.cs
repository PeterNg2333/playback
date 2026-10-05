using System.Collections.Concurrent;
using System.Text.Json;
using Playback.Api.Notes;
using Playback.Api.Providers;

// A note model that writes each input source back as one cited point, so note checks can follow every
// source without a provider. Switches make it fail, defer, or omit citations on purpose.
sealed class FixtureNoteModel : GeminiLanguageModel
{
    static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public override bool IsConfigured => true;
    public int Calls;
    public bool FailNext, DeferAll, DeferLast, OmitInlineCitations;
    public Func<string, Task>? BeforeFinish;
    public readonly ConcurrentBag<int> InputLengths = new();

    public override async Task<string> Generate(string name, string instructions, string prompt, CancellationToken ct, string? model = null,
        Action<string>? onUpdate = null, Action<string>? onUsage = null)
    {
        Interlocked.Increment(ref Calls);
        if (FailNext)
        {
            FailNext = false;
            throw new TimeoutException("Synthetic generation timeout");
        }
        using var doc = JsonDocument.Parse(prompt);
        var root = doc.RootElement;
        var pending = root.GetProperty("pending");
        InputLengths.Add(prompt.Length);
        Expect.That(instructions == NoteInstructions.For(root.GetProperty("language").GetString()!) +
            (name == "LectureSectionOrganizer" ? "\n" + NoteInstructions.Organize : ""), "Effective note prompt did not reach the generation call");
        var inputs = pending.EnumerateArray().Concat(root.GetProperty("materials").EnumerateArray()).ToList();
        var points = (DeferLast ? inputs.SkipLast(1) : inputs).Select(x => new
        {
            text = x.GetProperty("text").GetString() + (OmitInlineCitations ? "" : " [" + x.GetProperty("id").GetString() + "]"),
            sourceIds = new[] { x.GetProperty("id").GetString()! },
            retains = Array.Empty<string>()
        }).ToArray();
        var result = DeferAll
            ? JsonSerializer.Serialize(new
            {
                sections = Array.Empty<object>(),
                deferred = inputs.Select(x => new { sourceId = x.GetProperty("id").GetString(), reason = "Needs a complete continuation" })
            }, JsonOptions)
            : JsonSerializer.Serialize(new
            {
                sections = new[]
                {
                    new { id = "new", baseVersion = 0, title = "Fixture topic", markdown = "## Fixture topic\n\n" + string.Join("\n\n", points.Select(x => x.text)), points }
                },
                deferred = (DeferLast ? inputs.TakeLast(1) : []).Select(x => new { sourceId = x.GetProperty("id").GetString(), reason = "Needs a complete continuation" })
            }, JsonOptions);
        if (name == "LectureSectionOrganizer")
        {
            var section = root.GetProperty("editableSections")[0];
            var organized = section.GetProperty("points").EnumerateArray().Select(x => new
            {
                text = x.GetProperty("text").GetString(),
                sourceIds = x.GetProperty("sourceIds").EnumerateArray().Select(s => s.GetString()).ToArray(),
                retains = new[] { x.GetProperty("id").GetString() }
            }).ToArray();
            result = JsonSerializer.Serialize(new
            {
                sections = new[]
                {
                    new
                    {
                        id = section.GetProperty("id").GetString(),
                        baseVersion = section.GetProperty("version").GetInt32(),
                        title = "Organized sharing",
                        markdown = "## Organized sharing\n\n" + string.Join("\n\n", organized.Select(x => x.text)),
                        points = organized
                    }
                },
                deferred = Array.Empty<object>()
            }, JsonOptions);
        }
        onUsage?.Invoke("{\"input_tokens\":20,\"output_tokens\":12}");
        onUpdate?.Invoke(result);
        var id = pending.GetArrayLength() > 0 ? pending[0].GetProperty("sourceId").GetString()! : "";
        if (BeforeFinish is not null) await BeforeFinish(id);
        return result;
    }
}
