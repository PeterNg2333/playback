using System.Diagnostics;
using System.Text.Json.Nodes;
using Playback.Application.Notes;
using Playback.Api.Providers;

namespace Playback.Api.Notes;

// The OpenRouter note writer with model fallback, retries and a Gemini fallback (decided 2026-10-19: option 2, and
// 2026-10-20: Qwen, then GLM, then Gemini). A transient failure (OpenRouterTextModel.IsTransient: timeout or stall,
// dropped connection, HTTP 408/429/5xx, provider error mid-reply) moves to the next OpenRouter model at once; after the
// last model the attempts start again from the first, after a short wait or the provider's Retry-After. When the
// attempts or the retry window run out, Gemini writes the same request with the same schema and caps. Any other
// failure (bad key, no credits, refusal, output limit, a reply that does not parse) fails the call as before, because
// another attempt or model would hide the cause. A caller's cancellation is never retried.
//
// Each OpenRouter model is its own request with its own reasoning setting. OpenRouter's single-request "models" list
// cannot do this: it sends one reasoning setting to every model, and GLM-5.3-Flash refuses reasoning "none" (HTTP 400
// "Reasoning is mandatory for this endpoint", live 2026-10-20), which Qwen needs to answer in about 10 s.
//
// Who actually wrote the reply travels with the usage report, the record the activity log already keeps per call:
// usage gains a "writer" object { provider, model, fallbackFrom?, failures: [{ model, error, usage? }] } whenever a
// call needed more than one attempt. NoteActivityAdapter shows that writer in the activity entry and saves it.
public sealed class FallbackNoteWriter(IReadOnlyList<OpenRouterNoteWriter> openRouter, INoteWriter fallback, FallbackNoteWriter.Policy? policy = null) : INoteWriter
{
    public FallbackNoteWriter(OpenRouterNoteWriter primary, INoteWriter fallback, Policy? policy = null) : this([primary], fallback, policy) { }

    // Attempts: OpenRouter calls before Gemini. Waits: before going back to a model already tried (the first time, the
    // second…). Window: no OpenRouter attempt starts later than this after the first, so a stalled provider costs one
    // call deadline, not three.
    public sealed record Policy(int Attempts, TimeSpan[] Waits, TimeSpan LongestRetryAfter, TimeSpan Window,
        Func<TimeSpan, CancellationToken, Task> Wait);
    public static readonly Policy Default = new(3, [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4)], TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(60), Task.Delay);

    readonly Policy rules = policy ?? Default;
    readonly OpenRouterNoteWriter primary = openRouter.Count > 0 ? openRouter[0] : throw new ArgumentException("An OpenRouter writer is required");
    public bool IsConfigured => primary.IsConfigured;
    public string Provider => primary.Provider;
    public string Model => primary.Model;
    public IReadOnlyList<string> Models => openRouter.Select(x => x.Model).ToList();

    public async Task<NotePatch> Generate(string name, string instructions, string input, CancellationToken ct,
        Action<string>? onUpdate = null, Action<string>? onUsage = null)
    {
        var failures = new JsonArray();
        var since = Stopwatch.StartNew();
        var retryAfter = new Dictionary<OpenRouterNoteWriter, TimeSpan>();
        Exception? last = null;
        for (var attempt = 1; attempt <= rules.Attempts; attempt++)
        {
            var current = openRouter[(attempt - 1) % openRouter.Count];
            if (attempt > 1)
            {
                // Another model starts at once; going back to a model already tried waits first.
                var repeat = attempt - openRouter.Count;
                var wait = repeat < 1 ? TimeSpan.Zero : retryAfter.TryGetValue(current, out var after) ? after
                    : rules.Waits[Math.Min(repeat - 1, rules.Waits.Length - 1)];
                if (since.Elapsed + wait > rules.Window) break;
                // The draft restarts with the next attempt; the activity shows who writes it and why the last one stopped.
                onUsage?.Invoke(Writer(current, current == primary ? null : primary, failures).ToJsonString());
                onUpdate?.Invoke("");
                if (wait > TimeSpan.Zero) await rules.Wait(wait, ct);
            }
            JsonNode? usage = null;
            try
            {
                return await current.Generate(name, instructions, input, ct, onUpdate, report =>
                {
                    usage = JsonNode.Parse(report);
                    onUsage?.Invoke(failures.Count == 0 ? report : WithWriter(report, Writer(current, current == primary ? null : primary, failures)));
                });
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && OpenRouterTextModel.IsTransient(ex))
            {
                last = ex;
                failures.Add(new JsonObject { ["model"] = current.Model, ["error"] = ex.Message, ["usage"] = usage?.DeepClone() });
                if (ex is OpenRouterTransientException { RetryAfter: { } after })
                    retryAfter[current] = TimeSpan.FromTicks(Math.Clamp(after.Ticks, 0, rules.LongestRetryAfter.Ticks));
            }
        }
        var tries = failures.Count;
        if (!fallback.IsConfigured)
            throw new InvalidOperationException($"{last!.Message} ({tries} OpenRouter attempts; no Gemini key for a fallback)", last);
        var writer = Writer(fallback, primary, failures);
        onUsage?.Invoke(writer.ToJsonString());
        onUpdate?.Invoke("");
        return await fallback.Generate(name, instructions, input, ct, onUpdate, report => onUsage?.Invoke(WithWriter(report, writer)));
    }

    static JsonObject Writer(INoteWriter actual, INoteWriter? replaced, JsonArray failures)
    {
        var writer = new JsonObject { ["provider"] = actual.Provider, ["model"] = actual.Model };
        if (replaced is not null) writer["fallbackFrom"] = replaced.Provider + "/" + replaced.Model;
        writer["failures"] = failures.DeepClone();
        return new JsonObject { ["writer"] = writer };
    }

    static string WithWriter(string usage, JsonObject writer)
    {
        if (JsonNode.Parse(usage) is not JsonObject record) return writer.ToJsonString();
        record["writer"] = writer["writer"]!.DeepClone();
        return record.ToJsonString();
    }

}

// What the activity entry shows about who answered a note call, read from its usage report: the writer after retries
// or the Gemini fallback ("writer", FallbackNoteWriter), the model OpenRouter actually used (its "models" fallback),
// and the provider that served it. Null when the usage says none of these.
public static class NoteWriterReport
{
    public static (string Provider, string Model, string Note)? Describe(string? usageJson, string requestedModel)
    {
        if (usageJson is null) return null;
        JsonObject? root;
        try { root = JsonNode.Parse(usageJson) as JsonObject; }
        catch (System.Text.Json.JsonException) { return null; }
        if (root is null) return null;
        string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        var notes = new List<string>();
        var provider = "openrouter";
        var model = requestedModel;
        if (root["writer"] is JsonObject writer && Text(writer["provider"]) is { } writerProvider && Text(writer["model"]) is { } writerModel)
        {
            (provider, model) = (writerProvider, writerModel);
            var errors = writer["failures"] is JsonArray failures ? failures.Select(x => Text(x?["error"])).OfType<string>().ToList() : [];
            var reasons = errors.Count == 0 ? "" : ": " + string.Join("; ", errors.Distinct());
            notes.Add(Text(writer["fallbackFrom"]) is { } from
                ? $"Fallback: written by {provider}/{model} after {from} failed {errors.Count} time{(errors.Count == 1 ? "" : "s")}{reasons}"
                : $"Retried: {provider}/{model} after {errors.Count} failed attempt{(errors.Count == 1 ? "" : "s")}{reasons}");
        }
        if (provider == "openrouter")
        {
            var served = Text(root["provider"]);
            // OpenRouter may report a dated slug (qwen/qwen3.8-flash-20260826) for the model that was asked for.
            if (Text(root["model"]) is { } answered && answered != model && !answered.StartsWith(model + "-", StringComparison.Ordinal))
            {
                notes.Add($"Model fallback: {answered} answered for {model}" + (served is null ? "" : $", served by {served}"));
                model = answered;
            }
            else if (served is not null) notes.Add("Served by " + served);
        }
        return notes.Count == 0 ? null : (provider, model, string.Join(" · ", notes));
    }
}
