using Playback.Domain.Notes;

namespace Playback.Application.Notes;

// Purpose-specific boundaries; no provider or storage types cross them.
public interface INoteRepository
{
    Task<List<Note>> History(string id);
    Task<Note?> Version(string id, int version);
    Task<object> Page(string id, int? before, int limit);
    Task<NoteGateRecord?> Gate(string id);
    Task SaveGate(NoteGateRecord state);
    Task AcknowledgeFlush(string id, int version);
    Task<object> Save(string id, SectionUpdate update, string inputHash, int basedOnVersion, string language,
        string author = "agent", List<string>? deleted = null, List<string>? suppressed = null);
}

public interface INoteEvidence
{
    Task<NoteSession?> Session(string id);
    Task<NoteProvenance> Provenance(string id, IReadOnlyCollection<string> sourceIds);
    Task MarkNotes(IEnumerable<string> ids, string status, string? error = null);
}

// Narrow persistence read: save must not reload a three-hour session or recompute all term candidates.
public sealed record NoteSourceTiming(long StartMs, long EndMs, DateTime? RecordedAt);
public sealed record NoteProvenance(List<string> MaterialIds, List<NoteSourceTiming> Transcripts);

public interface INoteSettings
{
    Task<string> Language(string id);
    Task<string> Presentation(string id) => Task.FromResult(ProcessingPolicy.Defaults.NotePresentation);
}
public interface INoteOrganizationRepository
{
    Task<NoteOrganizationState?> Organization(string sessionId);
    Task SaveOrganization(NoteOrganizationState state, int expectedRevision);
}
public interface INoteRewriteRepository
{
    Task<List<NoteRewriteProposal>> Rewrites(string sessionId);
    Task<NoteRewriteProposal?> Rewrite(string sessionId, string proposalId);
    Task SaveRewrite(NoteRewriteProposal proposal);
}
public interface INoteSpeechBoundaries
{
    Task<Playback.Domain.Audio.SpeechBoundary?> Latest(string sessionId);
}

public interface INoteWriter
{
    bool IsConfigured { get; }
    // Saved in AI activity so a note version names who actually wrote it, e.g. "vertex" or "openrouter".
    string Provider { get; }
    string Model { get; }
    Task<NotePatch> Generate(string name, string instructions, string input, CancellationToken ct,
        Action<string>? onUpdate = null, Action<string>? onUsage = null);
    // The reply text for one task; the caller parses it against task.Schema.
    Task<string> Complete(NoteTask task, string instructions, string input, CancellationToken ct,
        Action<string>? onUpdate = null, Action<string>? onUsage = null);
}

// Puts note text into the session's script after the model writes it, e.g. Simplified characters that slip into
// Traditional notes. Returns the text unchanged when nothing needs converting.
public interface INoteScript
{
    string Apply(string text, string language);
}

public interface INoteGate
{
    bool IsConfigured { get; }
    Task<NoteGateDecision> Decide(string input, CancellationToken ct);
}

public sealed record NoteGateDecision(bool Allow, double Probability, double Confidence, string Model, string? UsageJson, long LatencyMs,
    string? Action = null);

// Only the mutable fields needed by note orchestration; the adapter owns the saved activity document.
public sealed class NoteActivity
{
    public required string Id { get; init; }
    public DateTime StartedAt { get; init; }
    public string? SectionId { get; set; }
    public string? UsageJson { get; set; }
    public long? ProviderLatencyMs { get; set; }
    public long? ScheduleDelayMs { get; set; }
}

public interface INoteActivity
{
    Task<NoteActivity> Begin(string sessionId, string task, string provider, string model,
        IEnumerable<string>? sources = null, int? basedOnVersion = null);
    Task Start(NoteActivity item);
    void Context(NoteActivity item, string version, string instructions, string input);
    void Draft(NoteActivity item, string text);
    Task End(NoteActivity item, string status, string summary, string? model = null);
    Task Fail(NoteActivity item, Exception error);
    void Warning(string message, string? sessionId, Exception error);
}
