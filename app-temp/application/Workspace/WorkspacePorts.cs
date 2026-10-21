using Playback.Application.Notes;
using Playback.Domain.Evidence;
using Playback.Domain.Models;
using Playback.Domain.Workspace;

namespace Playback.Application.Workspace;
public interface IWorkspaceRepository
{
    Task<object> CreateSession(string title, string? groupId = null);
    Task<List<SessionRecord>> Sessions();
    Task<List<GroupRecord>> Groups();
    Task<GroupRecord> CreateGroup(string name);
    Task<GroupRecord> RenameGroup(string id, string name);
    Task DeleteGroup(string id);
    Task<object> MoveSession(string id, string? groupId);
    Task<SessionRecord> SetLanguages(string id, string asrLanguage, string noteLanguage, string? asrModel = null);
    Task<SessionRecord> SetTranslation(string id, bool enabled, string language);
    Task RetryTranslations(string id);
    Task<SessionView?> Session(string id);
    Task<List<Transcript>> AllTranscripts(string id);
    Task<object> AddMaterial(string id, string name, string text);
    Task<object> SyncSession(string id, string? cursor);
    Task RequireSessionOwner(string id);
    Task GetOrCreateDemoUser(string username, CancellationToken ct);
}
public sealed class WorkspaceCommands(IWorkspaceRepository repository)
{
    public Task<object> CreateSession(string title, string? groupId = null) => repository.CreateSession(WorkspaceRules.Title(title), groupId);
    public Task<List<SessionRecord>> Sessions() => repository.Sessions();
    public Task<List<GroupRecord>> Groups() => repository.Groups();
    public Task<GroupRecord> CreateGroup(string name) => repository.CreateGroup(WorkspaceRules.GroupName(name));
    public Task<GroupRecord> RenameGroup(string id, string name) => repository.RenameGroup(id, WorkspaceRules.GroupName(name));
    public Task DeleteGroup(string id) => repository.DeleteGroup(id);
    public Task<object> MoveSession(string id, string? groupId) => repository.MoveSession(id, groupId);
    public Task<SessionRecord> SetLanguages(string id, string asrLanguage, string noteLanguage, string? asrModel = null)
    {
        AsrLanguage.Validate(asrLanguage); NoteLanguage.ValidateNote(noteLanguage); AsrModelOptions.Validate(asrModel);
        return repository.SetLanguages(id, asrLanguage, noteLanguage, asrModel);
    }
    public Task<SessionRecord> SetTranslation(string id, bool enabled, string language) => repository.SetTranslation(id, enabled, language);
    public async Task RetryTranslations(string id)
    {
        var session = await repository.Session(id) ?? throw new InvalidOperationException("Session not found");
        if (!session.TranslationEnabled) throw new InvalidOperationException("Translation is disabled");
        await repository.RetryTranslations(id);
    }
    public Task<SessionView?> Session(string id) => repository.Session(id);
    // The trace graph for a session, or the neighbourhood of one chunk, segment, topic, block or term.
    public async Task<LineageView> Lineage(string id, string? node, int depth)
    {
        var session = await repository.Session(id) ?? throw new WorkspaceNotFoundException();
        return LineageGraph.Build(session.Chunks, await repository.AllTranscripts(id), session.CurrentNote, session.TermInsights, node, depth);
    }
    public Task<object> AddMaterial(string id, string name, string text) {WorkspaceRules.Material(name, text); return repository.AddMaterial(id, name, text);}
    public Task<object> SyncSession(string id, string? cursor) => repository.SyncSession(id, cursor);
}
