using System.Collections.Concurrent;
using Playback.Api.Db;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Playback.Api.Activity;

// Execution state belongs here; note versions and Jev decisions remain their own saved results.
public sealed class AiActivity(PlaybackStore store)
{
    // Notes needs live draft/lifecycle metadata; complete prompts remain in storage
    // and the group flow response. Use the same record contract without a second DTO.
    public static readonly JsonSerializerOptions NotesResponseOptions = new(JsonSerializerDefaults.Web) {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { type => {
            if (type.Type == typeof(ActivityRecord))
                foreach (var property in type.Properties.Where(x => x.Name == "promptText").ToList()) type.Properties.Remove(property);
        } } }
    };
    readonly DateTime startedAt = DateTime.UtcNow;
    readonly ConcurrentDictionary<string, ActivityRecord> running = new();
    readonly object lifecycle = new();
    readonly HashSet<string> deletedSessions = [];
    public async Task<ActivityRecord> Begin(string sessionId, string task, string provider, string model,
        IEnumerable<string>? sources = null, int? basedOnVersion = null)
    {
        var item = new ActivityRecord { Id = Guid.NewGuid().ToString("N"), SessionId = sessionId,
            Task = task, Provider = provider, Model = model, Status = "queued", StartedAt = DateTime.UtcNow,
            SourceIds = sources?.Distinct().ToList() ?? [], BasedOnVersion = basedOnVersion };
        lock (lifecycle)
        {
            if (deletedSessions.Contains(sessionId)) throw new InvalidOperationException("Session was deleted");
            running[item.Id] = item;
        }
        try { await store.SaveActivity(item); }
        catch { running.TryRemove(item.Id, out _); throw; }
        return item;
    }
    // Admit deletion atomically with background work, so a queued provider call cannot
    // begin halfway through removing the session's recordings and saved results.
    public async Task DeleteSession(string sessionId)
    {
        lock (lifecycle)
        {
            if (running.Values.Any(x => x.SessionId == sessionId))
                throw new InvalidOperationException("Wait for session processing to finish before deleting it");
            if (!deletedSessions.Add(sessionId)) throw new InvalidOperationException("Session was deleted");
        }
        try { await store.DeleteSession(sessionId); }
        catch
        {
            lock (lifecycle) deletedSessions.Remove(sessionId);
            throw;
        }
    }
    public async Task Start(ActivityRecord item)
    {
        item.QueueDelayMs = (long)(DateTime.UtcNow - item.StartedAt).TotalMilliseconds;
        item.Status = "running";
        await store.SaveActivity(item);
    }
    public void Context(ActivityRecord item, string version, string instructions, string input) {
        item.PromptVersion = version; item.PromptHash = ContentHash.Of(instructions);
        item.PromptText = instructions;
        item.InputHash = ContentHash.Of(input); item.InputBytes = System.Text.Encoding.UTF8.GetByteCount(input);
    }
    public void Draft(ActivityRecord item, string text)
    {
        if (text.Length <= 250_000) item.Draft = text;
    }
    public async Task End(ActivityRecord item, string status, string summary, string? model = null)
    {
        item.Status = status; item.Summary = summary; item.Model = model ?? item.Model;
        item.EndedAt = DateTime.UtcNow;
        item.DurationMs = (long)(item.EndedAt.Value - item.StartedAt).TotalMilliseconds;
        item.Draft = null;
        await store.SaveActivity(item);
        running.TryRemove(item.Id, out _);
    }
    public Task Fail(ActivityRecord item, Exception error) => End(item, "failed", SafeError(error));
    public async Task<List<ActivityRecord>> Read(string sessionId)
    {
        var saved = await store.ActivityHistory(sessionId);
        foreach (var item in saved.Where(x => (x.Status is "queued" or "running") && x.StartedAt < startedAt && !running.ContainsKey(x.Id)))
            await End(item, "failed", "API restarted before this operation completed; inspect the saved result before retrying.");
        return saved.Select(x => running.TryGetValue(x.Id, out var live) ? live : x).ToList();
    }
    public static string SafeError(Exception error)
    {
        if (error is OperationCanceledException or TimeoutException) return "Request timed out or was cancelled; retry explicitly.";
        var value = error.Message;
        foreach (var name in new[] { "OPENROUTER_API_KEY", "GOOGLE_AI_STUDIO_API_KEY", "JEV_API_KEY", "DASHSCOPE_API_KEY" })
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } key) value = value.Replace(key, "[redacted]");
        return System.Text.RegularExpressions.Regex.Replace(value[..Math.Min(value.Length, 600)],
            @"(?i)(bearer\s+|[?&](?:key|api_key)=)[^\s&]+", "$1[redacted]");
    }
}
