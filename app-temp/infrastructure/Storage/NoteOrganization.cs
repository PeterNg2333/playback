using MongoDB.Driver;
using Playback.Domain.Notes;

namespace Playback.Api.Db;

public sealed record OrganizationStatusView(string Status, int DirtyRevisions, DateTime? DirtySince, DateTime? CooldownUntil, DateTime? LastOrganizedAt, string DueReason);
public partial class PlaybackStore
{
    public virtual Task<NoteOrganizationState?> NoteOrganization(string id) => Collection<NoteOrganizationState>("note_organization").Find(x => x.Id == id).FirstOrDefaultAsync()!;
    public virtual async Task SaveNoteOrganization(NoteOrganizationState state, int expectedRevision)
    {
        state.Revision = expectedRevision + 1;
        try
        {
            if (expectedRevision == 0) await Collection<NoteOrganizationState>("note_organization").InsertOneAsync(state);
            else if ((await Collection<NoteOrganizationState>("note_organization").ReplaceOneAsync(x => x.Id == state.Id && x.Revision == expectedRevision, state)).MatchedCount != 1)
                throw new InvalidOperationException("Organization state changed; retry against the latest state");
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { state.Revision = expectedRevision; throw new InvalidOperationException("Organization state changed; retry against the latest state"); }
        catch { state.Revision = expectedRevision; throw; }
        Touch(state.Id, "processing");
    }
    public virtual async Task<OrganizationStatusView?> NoteOrganizationStatus(string id)
    {
        var latest = await NoteRepository.Latest(id);
        if (latest is null) return null;
        var state = await NoteOrganization(id) ?? new() {Id = id};
        var sourceIds = latest.Sections.SelectMany(x => x.Blocks).SelectMany(x => x.Anchors).SelectMany(x => x.SourceIds).Distinct().ToList();
        var timings = await Collection<Transcript>("transcripts").Find(x => x.SessionId == id && sourceIds.Contains(x.Id))
            .Project(x => new { x.Id, x.StartMs }).ToListAsync();
        var spoken = timings.ToDictionary(x => x.Id, x => x.StartMs);
        var due = Playback.Domain.Notes.NoteOrganization.Due(latest, state, (await SessionProcessingSettings(id)).Effective, DateTime.UtcNow, spoken);
        return new(state.Status, due.DirtyRevisions, due.DirtySince, due.CooldownUntil, state.LastOrganizedAt, due.Reason ?? "");
    }
}
