using System.Diagnostics;
using System.Text.Json.Nodes;
using Playback.Application.Notes;
using Playback.Api.Providers;

namespace Playback.Api.Notes;

// The OpenRouter note writer with retries and a Gemini fallback (decided 2026-10-19: option 2). A transient failure
// (OpenRouterTextModel.IsTransient: timeout or stall, dropped connection, HTTP 408/429/5xx, provider error mid-reply)
// is retried after a short wait, or the provider's Retry-After when it sent one. When the attempts or the retry window
// run out, Gemini writes the same request with the same schema and caps. Any other failure (bad key, no credits,
// refusal, output limit, a reply that does not parse) fails the call as before, because another attempt or model
// would hide the cause. A caller's cancellation is never retried.
//
// Who actually wrote the reply travels with the usage report, the record the activity log already keeps per call:
// usage gains a "writer" object { provider, model, fallbackFrom?, failures: [{ error, usage? }] } whenever a call
// needed more than one attempt. NoteActivityAdapter shows that writer in the activity entry and saves it.
public sealed class FallbackNoteWriter(OpenRouterNoteWriter primary, INoteWriter fallback, FallbackNoteWriter.Policy? policy = null) : INoteWriter
{
    // Attempts: OpenRouter calls before falling back. Waits: before the 2nd, 3rd… attempt. Window: no OpenRouter attempt
    // starts later than this after the first, so a stalled provider costs one call deadline, not three.
    public sealed record Policy(int Attempts, TimeSpan[] Waits, TimeSpan LongestRetryAfter, TimeSpan Window,
        Func<TimeSpan, CancellationToken, Task> Wait);
    public static readonly Policy Default = new(3, [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4)], TimeSpan.FromSeconds(20),
        TimeSpan.FromSeconds(60), Task.Delay);

    readonly Policy rules = policy ?? Default;
    public bool IsConfigured => primary.IsConfigured;
    public string Provider => primary.Provider;
    public string Model => primary.Model;

    public async Task<NotePatch> Generate(string name, string instructions, string input, CancellationToken ct,
        Action<string>? onUpdate = null, Action<string>? onUsage = null)
    {
        var failures = new JsonArray();
        var since = Stopwatch.StartNew();
        Exception? last = null;
        for (var attempt = 1; attempt <= rules.Attempts; attempt++)
        {
            JsonNode? usage = null;
            try
            {
                return await primary.Generate(name, instructions, input, ct, onUpdate, report =>
                {
                    usage = JsonNode.Parse(report);
                    onUsage?.Invoke(failures.Count == 0 ? report : WithWriter(report, Writer(primary, null, failures)));
                });
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && OpenRouterTextModel.IsTransient(ex))
            {
                last = ex;
                failures.Add(new JsonObject { ["error"] = ex.Message, ["usage"] = usage?.DeepClone() });
                var wait = ex is OpenRouterTransientException { RetryAfter: { } after }
                    ? TimeSpan.FromTicks(Math.Clamp(after.Ticks, 0, rules.LongestRetryAfter.Ticks))
                    : rules.Waits[Math.Min(attempt - 1, rules.Waits.Length - 1)];
                if (attempt == rules.Attempts || since.Elapsed + wait > rules.Window) break;
                // The draft restarts with the next attempt; the activity shows why the first one stopped.
                onUsage?.Invoke(Writer(primary, null, failures).ToJsonString());
                onUpdate?.Invoke("");
                await rules.Wait(wait, ct);
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

    // What the activity entry shows for a call's usage: the writer that actually answered and how it got there, or
    // null when the first attempt of the configured writer answered.
    public static (string Provider, string Model, string Note)? Describe(string? usageJson)
    {
        if (usageJson is null || !usageJson.Contains("\"writer\"", StringComparison.Ordinal)) return null;
        JsonNode? root;
        try { root = JsonNode.Parse(usageJson); }
        catch (System.Text.Json.JsonException) { return null; }
        if (root?["writer"] is not JsonObject writer || writer["provider"]?.GetValue<string>() is not { } provider ||
            writer["model"]?.GetValue<string>() is not { } model) return null;
        var errors = writer["failures"] is JsonArray failures
            ? failures.Select(x => x?["error"]?.GetValue<string>()).Where(x => x is not null).ToList() : [];
        var reasons = errors.Count == 0 ? "" : ": " + string.Join("; ", errors.Distinct());
        var note = writer["fallbackFrom"]?.GetValue<string>() is { } from
            ? $"Fallback: written by {provider}/{model} after {from} failed {errors.Count} time{(errors.Count == 1 ? "" : "s")}{reasons}"
            : $"Retried: {provider}/{model} after {errors.Count} failed attempt{(errors.Count == 1 ? "" : "s")}{reasons}";
        return (provider, model, note);
    }
}
