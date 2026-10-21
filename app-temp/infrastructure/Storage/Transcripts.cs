using Playback.Application.Recording;
using Playback.Domain;
using Playback.Domain.Evidence;
using Playback.Domain.Models;
using System.Text.RegularExpressions;
using MongoDB.Driver;
using MongoDB.Bson.Serialization.Attributes;

namespace Playback.Api.Db;

public partial class PlaybackStore
{
    public async Task SaveTranscript(ChunkRecord chunk, string original, AsrModel? asr = null,
        string? displayOriginal = null, string? languageHint = null, string? detectedLanguage = null)
    {
        if (await Collection<Transcript>("transcripts").CountDocumentsAsync(x => x.Id == chunk.Id) == 0)
            await Collection<Transcript>("transcripts").InsertOneAsync(new Transcript
            {
                Id = chunk.Id,
                SessionId = chunk.SessionId,
                SourceId = chunk.SourceId,
                StartMs = chunk.StartMs,
                EndMs = chunk.EndMs,
                RecordedAt = chunk.RecordedAt,
                Original = original,
                ReviewStatus = string.IsNullOrWhiteSpace(original) ? "" : "pending",
                ConfirmedAt = DateTime.UtcNow,
                DisplayOriginal = displayOriginal,
                AsrLanguageHint = languageHint,
                AsrDetectedLanguage = detectedLanguage,
                AsrProvider = asr?.Provider,
                AsrModel = asr?.Model,
                Uncertain = Transcript.NeedsReview(original),
                RecognitionStatus = string.IsNullOrWhiteSpace(original)
                    ? "asr-empty"
                    : Transcript.NeedsReview(original) ? "review-needed" : "recognized",
                NoteStatus = string.IsNullOrWhiteSpace(original) ? "skipped" : "pending"
            });
        await Collection<ChunkRecord>("chunks").UpdateOneAsync(x => x.Id == chunk.Id,
            Builders<ChunkRecord>.Update
                .Set(x => x.Status, string.IsNullOrWhiteSpace(original) ? ChunkStatus.AsrEmpty : ChunkStatus.Transcribed)
                .Set(x => x.Error, null));
        Touch(chunk.SessionId, "source", [chunk.Id]);
        AudioChanged?.Invoke(chunk.SessionId);
        if (!string.IsNullOrWhiteSpace(original)) SourceChanged?.Invoke(chunk.SessionId);
    }
    public async Task<Transcript?> TranscriptForChunk(string id) =>
        await Collection<Transcript>("transcripts").Find(x => x.Id == id).FirstOrDefaultAsync();
    public virtual async Task<Dictionary<string, string>> Supersessions(string sessionId) =>
        Lineage.Supersessions(await Collection<Transcript>("transcripts")
            .Find(x => x.SessionId == sessionId && x.SupersededBy != null).ToListAsync());
    // Every transcript row of a session, superseded rows included, for trace and rerun planning.
    public virtual async Task<List<Transcript>> AllTranscripts(string sessionId) =>
        await Collection<Transcript>("transcripts").Find(x => x.SessionId == sessionId).SortBy(x => x.StartMs).ToListAsync();
    public async Task<SessionRecord> SetTranslation(string id, bool enabled, string language)
    {
        _ = LanguageSettings.OutputDescription(language);
        var session = await Collection<SessionRecord>("sessions").Find(x => x.Id == id).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Session not found");
        var changedLanguage = session.TranslationLanguage != language;
        var update = Builders<SessionRecord>.Update.Set(x => x.TranslationEnabled, enabled).Set(x => x.TranslationLanguage, language);
        await Collection<SessionRecord>("sessions").UpdateOneAsync(x => x.Id == id, update);
        if (changedLanguage)
        {
            await Collection<Transcript>("transcripts").UpdateManyAsync(x => x.SessionId == id,
                Builders<Transcript>.Update
                    .Set(x => x.TranslationStatus, "pending")
                    .Set(x => x.TranslationRetryAt, null)
                    .Set(x => x.TranslationError, null));
            Touch(id, "transcript");
        }
        Touch(id, "settings");
        return (await Collection<SessionRecord>("sessions").Find(x => x.Id == id).FirstAsync());
    }
    public async Task<List<Transcript>> PendingTranslations(int limit = 30, string? sessionId = null)
    {
        var sessions = await Collection<SessionRecord>("sessions")
            .Find(x => x.TranslationEnabled && (sessionId == null || x.Id == sessionId))
            .ToListAsync();
        var result = new List<Transcript>();
        foreach (var session in sessions)
        {
            var transcripts = await Collection<Transcript>("transcripts").Find(x => x.SessionId == session.Id && x.SupersededBy == null && x.Original != "" &&
                (x.TranslationLanguage != session.TranslationLanguage || x.TranslationStatus != "completed") &&
                (x.TranslationRetryAt == null || x.TranslationRetryAt <= DateTime.UtcNow))
                .SortBy(x => x.StartMs).Limit(limit - result.Count).ToListAsync();
            result.AddRange(transcripts);
            if (result.Count >= limit) break;
        }
        return result;
    }
    public async Task RetryTranslations(string id) {
        await Collection<Transcript>("transcripts").UpdateManyAsync(x => x.SessionId == id && x.TranslationStatus == "failed",
            Builders<Transcript>.Update.Set(x => x.TranslationRetryAt, null).Set(x => x.TranslationStatus, "pending"));
        Touch(id, "transcript");
    }
    public async Task<Transcript> SetTranslationResult(Transcript transcript, string language, string? value, string? error)
    {
        if (error is null && value is not null)
        {
            var originalHash = ContentHash.Of(transcript.Original);
            var versionInput = $"{transcript.SessionId}\n{transcript.Id}\n{language}\n{value}";
            var versionHash = ContentHash.Of(versionInput);
            var version = new TranslationVersion
            {
                Id = versionHash,
                SessionId = transcript.SessionId,
                TranscriptId = transcript.Id,
                Language = language,
                Text = value,
                OriginalHash = originalHash,
                CreatedAt = DateTime.UtcNow
            };
            try { await Collection<TranslationVersion>("translation_versions").InsertOneAsync(version); }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { }
        }
        var attempts = transcript.TranslationAttempts + 1;
        var retryAt = error is null
            ? (DateTime?)null
            : DateTime.UtcNow.AddSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(attempts - 1, 6))));
        var update = Builders<Transcript>.Update
            .Set(x => x.TranslationStatus, error is null ? "completed" : "failed")
            .Set(x => x.TranslationAttempts, attempts)
            .Set(x => x.TranslationError, error)
            .Set(x => x.TranslationRetryAt, retryAt);
        if (error is null) update = update.Set(x => x.Translation, value).Set(x => x.TranslationLanguage, language);
        var saved = await Collection<Transcript>("transcripts").FindOneAndUpdateAsync(x => x.Id == transcript.Id && x.Original == transcript.Original, update,
            new FindOneAndUpdateOptions<Transcript> { ReturnDocument = ReturnDocument.After })
            ?? throw new InvalidOperationException("Transcript changed during translation; stale result was not applied");
        Touch(transcript.SessionId, "translation", [transcript.Id]);
        return saved;
    }
}
