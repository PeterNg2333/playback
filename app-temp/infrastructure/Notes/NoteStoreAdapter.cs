using Playback.Application.Notes;
using Playback.Api.Db;

namespace Playback.Api.Notes;

// Bridge while non-note persistence remains in PlaybackStore. No admission/mutation rules here.
public sealed class NoteStoreAdapter(PlaybackStore store) : INoteRepository, INoteEvidence, INoteSettings, INoteProcessingPolicy, INoteSpeechBoundaries, INoteOrganizationRepository, INoteRewriteRepository
{
    public async Task<NoteSession?> Session(string id) => await store.Session(id) is { } session ? Read(session) : null;
    public Task<NoteProvenance> Provenance(string id, IReadOnlyCollection<string> sourceIds) => store.NoteProvenance(id, sourceIds);
    public async Task<string> Language(string id) => (await store.SessionSettings(id)).NoteLanguage;
    public async Task<string> Presentation(string id) => (await store.SessionProcessingSettings(id)).Effective.NotePresentation;
    public Task<Playback.Domain.Notes.NoteOrganizationState?> Organization(string id) => store.NoteOrganization(id);
    public Task SaveOrganization(Playback.Domain.Notes.NoteOrganizationState state, int expectedRevision) => store.SaveNoteOrganization(state, expectedRevision);
    public Task<List<Playback.Domain.Notes.NoteRewriteProposal>> Rewrites(string id) => store.NoteRewrites(id);
    public Task<Playback.Domain.Notes.NoteRewriteProposal?> Rewrite(string id, string proposalId) => store.NoteRewrite(id, proposalId);
    public Task SaveRewrite(Playback.Domain.Notes.NoteRewriteProposal proposal) => store.SaveNoteRewrite(proposal);
    public async Task<Playback.Domain.Notes.EffectiveProcessingPolicy> Effective(string id)
    {
        var value = await store.SessionProcessingSettings(id);
        return new(value.Effective, value.EffectiveRevision);
    }
    public Task MarkNotes(IEnumerable<string> ids, string status, string? error = null) => store.MarkNotes(ids, status, error);
    public Task<Playback.Domain.Audio.SpeechBoundary?> Latest(string id) => store.LatestSpeechBoundary(id);
    public Task<List<Note>> History(string id) => store.NoteHistory(id);
    public Task<Note?> Version(string id, int version) => store.NoteVersion(id, version);
    public Task<object> Page(string id, int? before, int limit) => store.NotePage(id, before, limit);
    public Task<NoteGateRecord?> Gate(string id) => store.NoteGate(id);
    public Task SaveGate(NoteGateRecord state) => store.SaveNoteGate(state);
    public Task AcknowledgeFlush(string id, int version) => store.AcknowledgeNoteFlush(id, version);
    public Task<object> Save(string id, SectionUpdate update, string inputHash, int basedOnVersion, string language,
        string author = "agent", List<string>? deleted = null, List<string>? suppressed = null) =>
        store.SaveSectionNote(id, update, inputHash, basedOnVersion, language, author, deleted, suppressed);

    public static NoteSession Read(SessionView session) => new(session.Id, session.NoteMarkdown, session.NoteVersion, session.NoteLanguage,
        session.CurrentNote, session.Transcripts.Select(Read).ToList(), session.Materials.Select(Read).ToList(),
        session.TermInsights.Select(x => x.Id).ToList(), session.Chunks.Any(x => ChunkStatus.AwaitingTranscript(x.Status)))
        { PendingAudio = session.Chunks.Where(x => ChunkStatus.AwaitingTranscript(x.Status)).Select(x => new PendingNoteAudio(x.SourceId, x.EndMs)).ToList() };
    public static NoteTranscript Read(Transcript x) => new() { Id = x.Id, SourceId = x.SourceId, StartMs = x.StartMs, EndMs = x.EndMs,
        RecordedAt = x.RecordedAt, SourceText = x.SourceText, Uncertain = x.Uncertain, NoteStatus = x.NoteStatus, ConfirmedAt = x.ConfirmedAt };
    public static NoteMaterial Read(Material x) => new() { Id = x.Id, SessionId = x.SessionId, Name = x.Name, Text = x.Text };
}
