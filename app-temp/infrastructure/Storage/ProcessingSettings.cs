using Playback.Application.Notes;
using Playback.Domain.Notes;
using Playback.Infrastructure.Notes;
using MongoDB.Driver;

namespace Playback.Api.Db;

// Stable UI projection: scheduler bookkeeping must not publish an idle delta.
public sealed record NoteAdmissionView(string Status, string AdmissionReason, string Decision, string PolicyRevision,
    DateTime? FirstPendingAt, DateTime? RetryAt, int Attempts, bool FlushRequested, string GateFailure)
{
    public static NoteAdmissionView? Read(NoteGateRecord? item) => item is null ? null :
        new(item.Status, item.AdmissionReason, item.Decision, item.PolicyRevision, item.FirstPendingAt,
            item.RetryAt, item.Attempts, item.FlushRequested, item.GateFailure);
}

public partial class PlaybackStore
{
    ProcessingSettings Processing => new(new MongoProcessingSettingsRepository(Collection<ProcessingSettingsRecord>("processing_settings")),
        processingDefaults);
    static string ProcessingOwner(string? owner) => owner is null ? "local" : "user:" + owner;
    public virtual Task<ProcessingSettingsView> WorkspaceProcessingSettings() => Processing.Workspace(ProcessingOwner(CurrentOwner));
    public virtual async Task<ProcessingSettingsView> SessionProcessingSettings(string id)
    {
        await RequireSessionOwner(id);
        var session = await SessionSettings(id);
        return await Processing.Session(ProcessingOwner(session.OwnerId), id);
    }
    public virtual async Task<ProcessingSettingsView> SaveWorkspaceProcessingSettings(ProcessingSettingsUpdate input)
    {
        var result = await Processing.SaveWorkspace(ProcessingOwner(CurrentOwner), input);
        // Signal only this workspace's sessions; unrelated workspaces retain their cursors.
        var owner = CurrentOwner;
        var sessions = await Collection<SessionRecord>("sessions").Find(x => x.OwnerId == owner).Project(x => x.Id).ToListAsync();
        foreach (var id in sessions) Touch(id, "processing");
        return result;
    }
    public virtual async Task<ProcessingSettingsView> SaveSessionProcessingSettings(string id, ProcessingSettingsUpdate input)
    {
        await RequireSessionOwner(id);
        var session = await SessionSettings(id);
        var result = await Processing.SaveSession(ProcessingOwner(session.OwnerId), id, input);
        Touch(id, "processing");
        return result;
    }
}
