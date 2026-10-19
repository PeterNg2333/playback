using System.Collections.Concurrent;
using Playback.Application.Notes;
using Playback.Api.Activity;
using Playback.Api.Db;
using Playback.Api.Providers;

namespace Playback.Api.Notes;

public sealed class GeminiNoteWriter(GeminiLanguageModel model) : INoteWriter
{
    public bool IsConfigured => model.IsConfigured;
    public string Provider => "vertex";
    public string Model => model.Model;
    public async Task<NotePatch> Generate(string name, string instructions, string input, CancellationToken ct,
        Action<string>? onUpdate = null, Action<string>? onUsage = null) =>
        NoteReplyParser.Parse(await model.Generate(name, instructions, input, ct, onUpdate: onUpdate, onUsage: onUsage));
}

// PLAYBACK_NOTE_MODEL picks the model that writes and organizes notes: "openrouter:<model>[@reasoning]" or
// "vertex:<model>". An OpenRouter writer retries transient failures and then falls back to the Gemini model
// (FallbackNoteWriter). Chat, translation and term explanations stay on Gemini (they use Google Search grounding).
public static class NoteWriters
{
    public const string Default = "openrouter:qwen/qwen3.8-flash@none";

    public static INoteWriter Create(GeminiLanguageModel gemini, FallbackNoteWriter.Policy? retries = null)
    {
        // Without a setting the default needs an OpenRouter key; with none, notes stay on the Gemini model.
        var choice = Environment.GetEnvironmentVariable("PLAYBACK_NOTE_MODEL") is { Length: > 0 } configured ? configured
            : OpenRouterKey.Read() is not null ? Default : "vertex:" + gemini.Model;
        var separator = choice.IndexOf(':');
        if (separator < 1) throw new InvalidOperationException("PLAYBACK_NOTE_MODEL must be provider:model");
        var (provider, model) = (choice[..separator], choice[(separator + 1)..]);
        return provider switch
        {
            "openrouter" => new FallbackNoteWriter(
                new OpenRouterNoteWriter(new OpenRouterTextModel(model.Split('@')[0], model.Contains('@') ? model[(model.IndexOf('@') + 1)..] : "low")),
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
    public async Task<NotePatch> Generate(string name, string instructions, string input, CancellationToken ct,
        Action<string>? onUpdate = null, Action<string>? onUsage = null)
    {
        var reply = await model.Generate(name, instructions, input, ct, onUsage, onUpdate ?? (_ => { }));
        return NoteReplyParser.Parse(reply);
    }
}

// The activity entry names the writer that actually answered: when the usage report says a call was retried or fell
// back (FallbackNoteWriter), the entry's provider and model become that writer's and its summary says why, both live
// in the popover and in the saved record.
public sealed class NoteActivityAdapter(AiActivity activity, ILogger<NoteAgent> logger) : INoteActivity
{
    readonly ConcurrentDictionary<string, ActivityRecord> records = new();
    public async Task<NoteActivity> Begin(string sessionId, string task, string provider, string model,
        IEnumerable<string>? sources = null, int? basedOnVersion = null)
    {
        var record = await activity.Begin(sessionId, task, provider, model, sources, basedOnVersion);
        records[record.Id] = record;
        return new NoteActivity { Id = record.Id, StartedAt = record.StartedAt };
    }
    ActivityRecord Record(NoteActivity item)
    {
        var record = records[item.Id];
        record.SectionId = item.SectionId;
        if (record.UsageJson != item.UsageJson && FallbackNoteWriter.Describe(item.UsageJson) is var (provider, model, _))
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
        if (FallbackNoteWriter.Describe(record.UsageJson) is var (_, _, note)) summary = summary + " · " + note;
        await activity.End(record, status, summary, model);
        records.TryRemove(item.Id, out _);
    }
    public Task Fail(NoteActivity item, Exception error) => End(item, "failed", AiActivity.SafeError(error));
    public void Warning(string message, string? sessionId, Exception error) =>
        logger.LogWarning("{Message} {Session}: {Error}", message, sessionId, AiActivity.SafeError(error));
}
