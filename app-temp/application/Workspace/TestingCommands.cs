using Playback.Api.Db;
namespace Playback.Application.Workspace;
public interface ITestingRepository
{
    Task AddSyntheticTranscript(string id, string chunkId, string text);
    Task<List<Transcript>> PendingTranslations(int limit = 30, string? sessionId = null);
    Task DeleteTestSession(string id);
    Task DeleteTestGroup(string id);
}
public sealed class TestingCommands(ITestingRepository repository)
{
    public Task AddSyntheticTranscript(string id, string chunkId, string text) => repository.AddSyntheticTranscript(id, chunkId, text);
    public Task<List<Transcript>> PendingTranslations(int limit = 30, string? sessionId = null) => repository.PendingTranslations(limit, sessionId);
    public Task DeleteTestSession(string id) => repository.DeleteTestSession(id);
    public Task DeleteTestGroup(string id) => repository.DeleteTestGroup(id);
}
