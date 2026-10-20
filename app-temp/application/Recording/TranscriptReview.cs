using Playback.Api.Activity;
using Playback.Api.Audio.Asr;
using Playback.Api.Db;

namespace Playback.Application.Recording;

public sealed record TranscriptReviewItem(ChunkRecord Chunk, Transcript Transcript);
public interface ITranscriptReviewRepository
{
    Task<List<TranscriptReviewItem>> Pending(DateTime now);
    Task<SessionRecord> Settings(string sessionId);
    Task<bool> Save(TranscriptReviewItem item, AsrResult result, string display);
    Task Failed(TranscriptReviewItem item, string error, DateTime now);
}

// One second pass per newly confirmed chunk, admitted in source/stream batches. Stable chunk identities
// keep citations and playback valid; originals are retained and only changed wording reopens note work.
public sealed class TranscriptReview(ITranscriptReviewRepository repository, IAsrAdapter asr,
    ITranscriptionAudio audio, AiActivity activity, Func<string, CancellationToken, Task> notes,
    Func<string, Func<Task<bool>>, CancellationToken, Task<bool>> apply)
{
    public const int BatchChunks = 4;
    public const int MaxBatchChunks = 8;
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    public static List<List<TranscriptReviewItem>> Due(IEnumerable<TranscriptReviewItem> items, DateTime now) =>
        items.GroupBy(x => (x.Chunk.SessionId, x.Chunk.SourceId, x.Chunk.StreamId))
            .Select(x => x.OrderBy(c => c.Chunk.StartMs).ThenBy(c => c.Chunk.Sequence).Take(MaxBatchChunks).ToList())
            .Where(x => x.Count >= BatchChunks || x.Any(c => c.Transcript.ConfirmedAt is { } at && at + Wait <= now)).ToList();

    public async Task Run(DateTime now, CancellationToken ct)
    {
        foreach (var batch in Due(await repository.Pending(now), now))
        {
            ct.ThrowIfCancellationRequested();
            var sessionId = batch[0].Chunk.SessionId;
            var call = await activity.Begin(sessionId, "ASR batch review", "local", "batch policy", batch.Select(x => x.Chunk.Id));
            activity.Context(call, "asr-review-v1", "Re-transcribe saved chunks after 30 seconds or four chunks; preserve identity and first-pass text.",
                string.Join("|", batch.Select(x => x.Chunk.Id)));
            var changed = 0;
            var needsNotes = false;
            var succeeded = 0;
            var failed = 0;
            try
            {
                await activity.Start(call);
                var settings = await repository.Settings(sessionId);
                foreach (var item in batch)
                {
                    ct.ThrowIfCancellationRequested();
                    ActivityRecord? recognition = null;
                    try
                    {
                        var chunk = item.Chunk;
                        recognition = await activity.Begin(sessionId, "ASR re-transcription", asr.Model.Provider,
                            settings.AsrModel ?? asr.Model.Model, [chunk.Id]);
                        activity.Context(recognition, "asr-review-v1", "Second recognition of the same saved chunk; no lecture-note prompt.", chunk.Id);
                        await activity.Start(recognition);
                        using var speech = await audio.Prepare(chunk.Path, ct);
                        // A second VAD pass must not erase words recognized on the first pass.
                        if (!speech.HasSpeech) throw new InvalidOperationException("Review found no speech; first-pass transcript retained");
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        var result = await asr.Transcribe(new(speech.Path, sessionId, chunk.SourceId, chunk.Sequence,
                            chunk.Hash, settings.AsrLanguage, settings.AsrModel, Pass: "review-v1"), ct);
                        if (string.IsNullOrWhiteSpace(result.Text)) throw new InvalidOperationException("Review returned no words; first-pass transcript retained");
                        var current = await repository.Settings(sessionId);
                        if (current.AsrLanguage != settings.AsrLanguage || current.AsrModel != settings.AsrModel)
                            throw new InvalidOperationException("ASR settings changed during review; first-pass transcript retained for a later review");
                        var display = audio.Display(result.Text, settings.AsrLanguage, result.Language);
                        var corrected = await apply(sessionId, () => repository.Save(item, result, display), ct);
                        if (corrected) { changed++; needsNotes |= item.Transcript.NoteStatus != "suppressed"; }
                        succeeded++;
                        recognition.UsageJson = result.UsageJson;
                        recognition.ProviderLatencyMs = watch.ElapsedMilliseconds;
                        await activity.End(recognition, "completed", (corrected ? "Corrected transcript saved; source identity retained" : "Review completed; wording unchanged") +
                            (result.FallbackSummary is null ? "" : "; " + result.FallbackSummary), result.Model?.Model);
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        failed++;
                        if (recognition is not null) await activity.Fail(recognition, ex);
                        await repository.Failed(item, AiActivity.SafeError(ex), now);
                    }
                    catch (Exception ex) { if (recognition is not null) await activity.Fail(recognition, ex); throw; }
                }
                // Persisted pending status also lets the normal scheduler recover a failed note call or restart.
                if (needsNotes) await notes(sessionId, ct);
                await activity.End(call, failed == batch.Count ? "failed" : "completed",
                    $"{succeeded} chunks reviewed; {failed} failed (first-pass text retained); {changed} corrected");
            }
            catch (Exception ex) { await activity.Fail(call, ex); throw; }
        }
    }
}
