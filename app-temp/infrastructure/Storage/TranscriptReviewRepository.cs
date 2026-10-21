using MongoDB.Driver;
using Playback.Api.Audio.Asr;
using Playback.Application.Recording;
using Playback.Domain.Models;

namespace Playback.Api.Db;

public partial class PlaybackStore
{
    public ITranscriptReviewRepository TranscriptReviews() => new ReviewRepository(this);
    sealed class ReviewRepository(PlaybackStore store) : ITranscriptReviewRepository
    {
        public async Task<List<TranscriptReviewItem>> Pending(DateTime now)
        {
            var rows = await store.Collection<Transcript>("transcripts").Find(x => x.ReviewStatus == "pending" && x.SupersededBy == null &&
                x.ReviewAttempts < 3 && (x.ReviewRetryAt == null || x.ReviewRetryAt <= now))
                .SortBy(x => x.ConfirmedAt).Limit(128).ToListAsync();
            var result = new List<TranscriptReviewItem>();
            foreach (var row in rows.Where(x => PlaybackEnvironment.AllowsAutomaticWork(x.SessionId)))
                if (await store.Chunk(row.Id) is { } chunk) result.Add(new(chunk, row));
            return result;
        }
        public Task<SessionRecord> Settings(string id) => store.SessionSettings(id);
        public async Task<bool> Save(TranscriptReviewItem item, AsrResult result, string display)
        {
            var before = item.Transcript;
            var changed = result.Text != before.Original;
            var update = Builders<Transcript>.Update.Set(x => x.ReviewStatus, "completed")
                .Set(x => x.ReviewedAt, DateTime.UtcNow).Set(x => x.ReviewRetryAt, null).Set(x => x.ReviewError, null);
            if (changed) update = update.Set(x => x.FirstPassOriginal, before.FirstPassOriginal ?? before.Original)
                .Set(x => x.Original, result.Text).Set(x => x.DisplayOriginal, display == result.Text ? null : display)
                .Set(x => x.AsrProvider, result.Model?.Provider).Set(x => x.AsrModel, result.Model?.Model)
                .Set(x => x.AsrDetectedLanguage, result.Language).Set(x => x.Uncertain, Transcript.NeedsReview(result.Text))
                .Set(x => x.RecognitionStatus, Transcript.NeedsReview(result.Text) ? "review-needed" : "recognized")
                .Set(x => x.NoteStatus, before.NoteStatus == "suppressed" ? "suppressed" : "pending").Set(x => x.NoteAttempts, 0)
                .Set(x => x.NoteRetryAt, null).Set(x => x.NoteError, null)
                .Set(x => x.Translation, null).Set(x => x.TranslationStatus, "pending").Set(x => x.TranslationAttempts, 0)
                .Set(x => x.TranslationRetryAt, null).Set(x => x.TranslationError, null);
            var saved = await store.Collection<Transcript>("transcripts").UpdateOneAsync(x => x.Id == before.Id &&
                x.Original == before.Original && x.ReviewStatus == "pending" && x.SupersededBy == null, update);
            if (saved.ModifiedCount == 0) return false;
            store.Touch(before.SessionId, "transcript", [before.Id]);
            return changed;
        }
        public async Task Failed(TranscriptReviewItem item, string error, DateTime now)
        {
            var attempts = item.Transcript.ReviewAttempts + 1;
            await store.Collection<Transcript>("transcripts").UpdateOneAsync(x => x.Id == item.Transcript.Id && x.ReviewStatus == "pending",
                Builders<Transcript>.Update.Set(x => x.ReviewAttempts, attempts).Set(x => x.ReviewError, error)
                    .Set(x => x.ReviewStatus, attempts >= 3 ? "failed" : "pending").Set(x => x.ReviewRetryAt, now.AddSeconds(30 * attempts)));
            store.Touch(item.Chunk.SessionId, "transcript", [item.Chunk.Id]);
        }
    }
}
