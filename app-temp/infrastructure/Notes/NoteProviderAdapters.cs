using System.Collections.Concurrent;
using Playback.Application.Notes;
using Playback.Api.Activity;
using Playback.Api.Db;
using Playback.Api.Providers;

namespace Playback.Api.Notes;

public sealed class GeminiNoteWriter(GeminiLanguageModel model) : INoteWriter
{
    public bool IsConfigured => model.IsConfigured;
    public string Model => model.Model;
    public async Task<NotePatch> Generate(string name, string instructions, string input, CancellationToken ct,
        Action<string>? onUpdate = null, Action<string>? onUsage = null) =>
        NoteReplyParser.Parse(await model.Generate(name, instructions, input, ct, onUpdate: onUpdate, onUsage: onUsage));
}

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
        await activity.End(Record(item), status, summary, model);
        records.TryRemove(item.Id, out _);
    }
    public Task Fail(NoteActivity item, Exception error) => End(item, "failed", AiActivity.SafeError(error));
    public void Warning(string message, string? sessionId, Exception error) =>
        logger.LogWarning("{Message} {Session}: {Error}", message, sessionId, AiActivity.SafeError(error));
}
