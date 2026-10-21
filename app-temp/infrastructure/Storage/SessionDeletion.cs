using MongoDB.Driver;

namespace Playback.Api.Db;

public partial class PlaybackStore
{
    public virtual async Task DeleteSession(string id)
    {
        if (!ChunkIdentity.IsSessionId(id)) throw new InvalidOperationException("Invalid session ID");
        var writeLock = noteWriteLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await writeLock.WaitAsync();
        try
        {
            _ = await SessionSettings(id);
            await DeleteSessionData(id);
        }
        finally { writeLock.Release(); }
    }

    async Task DeleteSessionData(string id)
    {
        var directory = Path.GetFullPath(Path.Combine(audioRoot, id));
        if (!directory.StartsWith(audioRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Audio path is outside the session directory");
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        var captureRoot = PlaybackEnvironment.CaptureFolder;
        var captureDirectory = Path.GetFullPath(Path.Combine(captureRoot, id));
        if (!captureDirectory.StartsWith(captureRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Capture path is outside the session directory");
        if (Directory.Exists(captureDirectory)) Directory.Delete(captureDirectory, recursive: true);
        var graphRoot = PlaybackEnvironment.GraphFolder;
        var graphDirectory = Path.GetFullPath(Path.Combine(graphRoot, id));
        if (graphDirectory.StartsWith(graphRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(graphDirectory))
            Directory.Delete(graphDirectory, recursive: true);
        await Collection<Transcript>("transcripts").DeleteManyAsync(x => x.SessionId == id);
        await Collection<TranslationVersion>("translation_versions").DeleteManyAsync(x => x.SessionId == id);
        await Collection<Note>("notes").DeleteManyAsync(x => x.SessionId == id);
        await Collection<NoteHead>("note_heads").DeleteOneAsync(x => x.Id == id);
        await Collection<TermInsight>("term_insights").DeleteManyAsync(x => x.SessionId == id);
        await Collection<Material>("materials").DeleteManyAsync(x => x.SessionId == id);
        await Collection<CitationRecord>("citations").DeleteManyAsync(x => x.SessionId == id);
        await Collection<ChunkRecord>("chunks").DeleteManyAsync(x => x.SessionId == id);
        await Collection<ConversationTurn>("conversation_turns").DeleteManyAsync(x => x.SessionId == id);
        await Collection<ConversationRecord>("conversations").DeleteManyAsync(x => x.SessionId == id);
        await Collection<ActivityRecord>("ai_activity").DeleteManyAsync(x => x.SessionId == id);
        await Collection<NoteGateRecord>("note_gates").DeleteManyAsync(x => x.SessionId == id);
        await Collection<Playback.Domain.Notes.NoteOrganizationState>("note_organization").DeleteOneAsync(x => x.Id == id);
        await Collection<Playback.Domain.Notes.NoteRewriteProposal>("note_rewrites").DeleteManyAsync(x => x.SessionId == id);
        await Collection<Playback.Domain.Audio.CombinedAudioSegment>("combined_audio").DeleteManyAsync(x => x.SessionId == id);
        await Collection<Playback.Domain.Audio.SpeechBoundary>("speech_boundaries").DeleteManyAsync(x => x.SessionId == id);
        await Collection<AudioStreamRecord>("audio_streams").DeleteManyAsync(x => x.SessionId == id);
        await Collection<RerunJob>("rerun_jobs").DeleteManyAsync(x => x.SessionId == id);
        await Collection<Playback.Application.Notes.ProcessingSettingsRecord>("processing_settings").DeleteOneAsync(x => x.Id == "session:" + id);
        await Collection<MongoDB.Bson.BsonDocument>("term_explanation_history").DeleteManyAsync(new MongoDB.Bson.BsonDocument("SessionId", id));
        await Collection<SessionRecord>("sessions").DeleteOneAsync(x => x.Id == id);
        syncLogs.TryRemove(id, out _);
        foreach (var reader in syncStates.Where(x => x.Value.SessionId == id)) syncStates.TryRemove(reader.Key, out _);
    }
}
