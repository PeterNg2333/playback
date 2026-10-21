using System.Collections.Concurrent;
using Playback.Application.Notes;
using Playback.Domain.Models;
using Playback.Application.Activity;
using Playback.Infrastructure.Providers;

namespace Playback.Infrastructure.Notes;

public sealed class GeminiNoteWriter(GeminiLanguageModel model) : INoteWriter
{
    public bool IsConfigured => model.IsConfigured;
    public string Provider => "vertex";
    public string Model => model.Model;
    public Task<string> Complete(NoteTask task, string instructions, string input, CancellationToken ct,
        Action<string>? onUpdate = null, Action<string>? onUsage = null) => model.Complete(task, instructions, input, ct, onUpdate, onUsage);
}

// PLAYBACK_NOTE_MODEL picks the model that writes and organizes notes: "openrouter:<model>[@reasoning]" or
// "vertex:<model>". An OpenRouter writer has three layers (FallbackNoteWriter): the model, then the models in
// ModelFallbacks, each through OpenRouter's provider routing, then the Gemini model. Chat, translation and term
// explanations stay on Gemini (they use Google Search grounding).
public static class NoteWriters
{
    public const string Default = "openrouter:qwen/qwen3.8-flash@none";

    // The OpenRouter model (and reasoning effort) that writes when a note model fails transiently. Qwen3.8 Flash has one
    // provider (Alibaba, whose shared pool returned 429 limit_burst_rate on 2026-10-19). GLM-5.3-Flash follows: it wrote
    // accurate notes in the 2026-10-18 comparison, has many providers, and needs reasoning (it refuses "none"; "low" is
    // what the comparison ran). DeepSeek V4.1 Flash is left out: its providers returned corrupted operations under the
    // edit contract, an unreadable reply is not retried, and Gemini is the better last layer (docs/history/phases/p4-results.zh-HK.md).
    public static readonly Dictionary<string, (string Model, string Reasoning)[]> ModelFallbacks = new()
    {
        ["qwen/qwen3.8-flash"] = [("z-ai/glm-5.3-flash", "low")]
    };

    public static INoteWriter Create(GeminiLanguageModel gemini, FallbackNoteWriter.Policy? retries = null)
    {
        // Without a setting the default needs an OpenRouter key; with none, notes stay on the Gemini model.
        var choice = Environment.GetEnvironmentVariable("PLAYBACK_NOTE_MODEL") is { Length: > 0 } configured ? configured
            : OpenRouterKey.Read() is not null ? Default : "vertex:" + gemini.Model;
        var separator = choice.IndexOf(':');
        if (separator < 1) throw new InvalidOperationException("PLAYBACK_NOTE_MODEL must be provider:model");
        var (provider, model) = (choice[..separator], choice[(separator + 1)..]);
        var name = model.Split('@')[0];
        return provider switch
        {
            "openrouter" => new FallbackNoteWriter(
                ModelFallbacks.GetValueOrDefault(name, []).Select(x => new OpenRouterNoteWriter(new OpenRouterTextModel(x.Model, x.Reasoning)))
                    .Prepend(new OpenRouterNoteWriter(new OpenRouterTextModel(name, model.Contains('@') ? model[(model.IndexOf('@') + 1)..] : "low")))
                    .ToList(),
                new GeminiNoteWriter(gemini), retries),
            "vertex" when model == gemini.Model => new GeminiNoteWriter(gemini),
            "vertex" => throw new InvalidOperationException("For a Vertex note model, set PLAYBACK_GEMINI_MODEL to the same allowlisted model"),
            _ => throw new InvalidOperationException("PLAYBACK_NOTE_MODEL provider must be openrouter or vertex")
        };
    }
}

// Streamed: the reply arrives as server-sent events and the live draft grows with it.
public sealed class OpenRouterNoteWriter(OpenRouterTextModel model) : INoteWriter
{
    public bool IsConfigured => model.IsConfigured;
    public string Provider => "openrouter";
    public string Model => model.Model;
    public Task<string> Complete(NoteTask task, string instructions, string input, CancellationToken ct,
        Action<string>? onUpdate = null, Action<string>? onUsage = null) =>
        model.Generate(task.Name, instructions, input, ct, onUsage, onUpdate ?? (_ => { }), task);
}

// The activity entry of an OpenRouter note call names who actually answered (NoteWriterReport): the entry's provider and
// model become the writer and model that answered (after retries, the fallback model or Gemini),
// and its summary says why and which provider served it, both live in the popover and in the saved record.
public sealed class NoteActivityAdapter(AiActivity activity, ILogger<NoteAgent> logger) : INoteActivity
{
    readonly ConcurrentDictionary<string, (ActivityRecord Record, string Model)> records = new();
    public async Task<NoteActivity> Begin(string sessionId, string task, string provider, string model,
        IEnumerable<string>? sources = null, int? basedOnVersion = null)
    {
        var record = await activity.Begin(sessionId, task, provider, model, sources, basedOnVersion);
        // Only OpenRouter calls carry this report; other tasks (Jev, local admission) keep what they began with.
        records[record.Id] = (record, provider == "openrouter" ? model : "");
        return new NoteActivity { Id = record.Id, StartedAt = record.StartedAt };
    }
    (string Provider, string Model, string Note)? Answered(string requested, string? usage) =>
        requested.Length == 0 ? null : NoteWriterReport.Describe(usage, requested);
    ActivityRecord Record(NoteActivity item)
    {
        var (record, requested) = records[item.Id];
        record.SectionId = item.SectionId;
        if (record.UsageJson != item.UsageJson && Answered(requested, item.UsageJson) is var (provider, model, _))
            (record.Provider, record.Model) = (provider, model);
        record.UsageJson = item.UsageJson;
        record.ProviderLatencyMs = item.ProviderLatencyMs;
        record.ScheduleDelayMs = item.ScheduleDelayMs;
        return record;
    }
    public Task Start(NoteActivity item) => activity.Start(Record(item));
    public void Context(NoteActivity item, string version, string instructions, string input) => activity.Context(Record(item), version, instructions, input);
    public void Draft(NoteActivity item, string text) => activity.Draft(Record(item), text);
    public async Task End(NoteActivity item, string status, string summary, string? model = null)
    {
        var record = Record(item);
        if (Answered(records[item.Id].Model, record.UsageJson) is var (_, _, note)) summary = summary + " · " + note;
        await activity.End(record, status, summary, model);
        records.TryRemove(item.Id, out _);
    }
    public Task Fail(NoteActivity item, Exception error) => End(item, "failed", AiActivity.SafeError(error));
    public void Warning(string message, string? sessionId, Exception error) =>
        logger.LogWarning("{Message} {Session}: {Error}", message, sessionId, AiActivity.SafeError(error));
}
