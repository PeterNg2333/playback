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
        var directory = Path.GetFullPath(Path.Combine(audioRoot, id));
        if (!directory.StartsWith(audioRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Audio path is outside the test directory");
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        var captureRoot = PlaybackEnvironment.CaptureFolder;
        var captureDirectory = Path.GetFullPath(Path.Combine(captureRoot, id));
        if (!captureDirectory.StartsWith(captureRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Capture path is outside the test directory");
        if (Directory.Exists(captureDirectory)) Directory.Delete(captureDirectory, recursive: true);
        await Collection<Transcript>("transcripts").DeleteManyAsync(x => x.SessionId == id);
        await Collection<TranslationVersion>("translation_versions").DeleteManyAsync(x => x.SessionId == id);
        await Collection<Note>("notes").DeleteManyAsync(x => x.SessionId == id);
        await Collection<TermInsight>("term_insights").DeleteManyAsync(x => x.SessionId == id);
        await Collection<Material>("materials").DeleteManyAsync(x => x.SessionId == id);
        await Collection<CitationRecord>("citations").DeleteManyAsync(x => x.SessionId == id);
        await Collection<ChunkRecord>("chunks").DeleteManyAsync(x => x.SessionId == id);
        await Collection<ConversationTurn>("conversation_turns").DeleteManyAsync(x => x.SessionId == id);
        await Collection<ConversationRecord>("conversations").DeleteManyAsync(x => x.SessionId == id);
        await Collection<SessionRecord>("sessions").DeleteOneAsync(x => x.Id == id);
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
