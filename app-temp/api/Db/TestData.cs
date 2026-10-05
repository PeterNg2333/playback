using MongoDB.Driver;

namespace Playback.Api.Db;

// Synthetic data the testing endpoints may add or remove; only E2E sessions and groups qualify.

public partial class PlaybackStore
{
    public async Task DeleteTestSession(string id)
    {
        if (!ChunkIdentity.IsSessionId(id)) throw new InvalidOperationException("Invalid session ID");
        var session = await Collection<SessionRecord>("sessions").Find(x => x.Id == id).FirstOrDefaultAsync();
        if (session is null || !session.Title.StartsWith("E2E demo ", StringComparison.Ordinal))
            throw new InvalidOperationException("Only E2E demo sessions can be removed by this endpoint");
        await DeleteSession(id);
    }

    public async Task DeleteTestGroup(string id)
    {
        var group = await Collection<GroupRecord>("groups").Find(x => x.Id == id).FirstOrDefaultAsync();
        if (group is null || !group.Name.StartsWith("E2E group ", StringComparison.Ordinal))
            throw new InvalidOperationException("Only E2E groups can be removed by this endpoint");
        if (await Collection<SessionRecord>("sessions").CountDocumentsAsync(x => x.GroupId == id) != 0)
            throw new InvalidOperationException("Group still contains sessions");
        await Collection<GroupRecord>("groups").DeleteOneAsync(x => x.Id == id);
    }
    public async Task AddSyntheticTranscript(string sessionId, string chunkId, string text)
    {
        var session = await Collection<SessionRecord>("sessions").Find(x => x.Id == sessionId).FirstOrDefaultAsync();
        if (session is null || !session.Title.StartsWith("E2E demo ", StringComparison.Ordinal) ||
            (text.Length > 0 && !text.StartsWith("Synthetic ", StringComparison.Ordinal)) || text.Length > 500)
            throw new InvalidOperationException("Only bounded synthetic E2E transcript text is allowed");
        var chunk = await Chunk(chunkId);
        if (chunk is null || chunk.SessionId != sessionId) throw new InvalidOperationException("Test chunk not found in session");
        await SaveTranscript(chunk, text);
    }
}
