using Playback.Application.Activity;
using Playback.Domain.Audio;
using Playback.Domain.Models;

namespace Playback.Application.Recording;

public sealed record RerunRequest(bool Asr = true, string? AsrModel = null, int WindowSeconds = 45, string Notes = "incremental");

public interface IRerunRepository
{
    Task<RerunJob?> Latest(string sessionId);
    Task<List<RerunJob>> Unfinished();
    Task Save(RerunJob job);
    Task<SessionRecord> Settings(string sessionId);
    Task<List<ChunkRecord>> Chunks(string sessionId);
    Task<List<Transcript>> Transcripts(string sessionId);
    // Inserts the segment and supersedes the window's rows, only while every one of them is still active.
    Task<bool> Supersede(RerunWindow window, Transcript segment);
}
public interface IRerunAudio
{
    // Concatenates the window's original chunk WAVs into one derived WAV and returns its path.
    Task<string> Combine(string sessionId, RerunWindow window, CancellationToken ct);
}

// A user-requested rerun: longer ASR windows supersede the chunk rows they re-recognize (stable chunk audio, new
// segment IDs, first-pass text retained on the segment), then notes are updated incrementally or rebuilt.
// Lineage.Apply maps notes, terms and saved answers that cite a superseded row to its segment.
public sealed class SessionRerun(IRerunRepository repository, IAsrAdapter asr, ITranscriptionAudio audio, IRerunAudio windows,
    AiActivity activity, Func<string, bool> recording,
    Func<string, Func<Task<bool>>, CancellationToken, Task<bool>> apply,
    Func<string, CancellationToken, Task> notes, Func<string, CancellationToken, Task> rebuild, TimeProvider? clock = null)
{
    public const int NotesEveryWindows = 8;
    public const int MaxAttempts = 3;
    readonly TimeProvider time = clock ?? TimeProvider.System;
    DateTime Now => time.GetUtcNow().UtcDateTime;

    public async Task<RerunJob> Start(string sessionId, RerunRequest request)
    {
        if (request.Notes is not ("incremental" or "rebuild" or "none")) throw new InvalidOperationException("Notes mode must be incremental, rebuild or none");
        if (!request.Asr && request.Notes == "none") throw new InvalidOperationException("Choose ASR, notes or both to rerun");
        if (request.Asr) { RerunWindows.Validate(request.WindowSeconds); AsrModelOptions.Validate(request.AsrModel); }
        if (recording(sessionId)) throw new InvalidOperationException("Stop recording before rerunning this session");
        if (await repository.Latest(sessionId) is { Finished: false }) throw new InvalidOperationException("A rerun is already running for this session");
        var settings = await repository.Settings(sessionId);
        var job = new RerunJob { Id = Guid.NewGuid().ToString("N"), SessionId = sessionId, Asr = request.Asr,
            AsrModel = request.AsrModel ?? settings.AsrModel, AsrLanguage = settings.AsrLanguage, WindowSeconds = request.WindowSeconds,
            Notes = request.Notes, NotesStatus = request.Notes == "none" ? "skipped" : "pending", CreatedAt = Now, UpdatedAt = Now };
        if (request.Asr)
        {
            var chunks = await repository.Chunks(sessionId);
            if (chunks.Any(x => ChunkStatus.AwaitingTranscript(x.Status))) throw new InvalidOperationException("Wait for pending transcription to finish before rerunning");
            job.Windows = RerunWindows.Plan(job.Id, chunks, await repository.Transcripts(sessionId), request.WindowSeconds);
            if (job.Windows.Count == 0) throw new InvalidOperationException("No saved speech to rerun");
        }
        job.Summary = request.Asr ? $"{job.Windows.Count} windows planned" : "Notes only";
        await repository.Save(job);
        return job;
    }

    public async Task<RerunJob?> Cancel(string sessionId)
    {
        var job = await repository.Latest(sessionId);
        if (job is null || job.Finished) return job;
        job.CancelRequested = true; job.UpdatedAt = Now;
        await repository.Save(job);
        return job;
    }

    public Task<RerunJob?> Status(string sessionId) => repository.Latest(sessionId);

    public async Task RunPending(CancellationToken ct)
    {
        foreach (var job in await repository.Unfinished()) await Run(job, ct);
    }

    public async Task Run(RerunJob job, CancellationToken ct)
    {
        var call = await activity.Begin(job.SessionId, "Session rerun", asr.Model.Provider, job.AsrModel ?? asr.Model.Model, job.Windows.SelectMany(x => x.ReplacesIds));
        activity.Context(call, "session-rerun-v1", $"Re-recognize saved audio in windows of about {job.WindowSeconds} s, then {job.Notes} notes. " +
            "Chunk audio stays; each window becomes one segment that supersedes its rows; first-pass text is retained.", job.Id);
        try
        {
            await activity.Start(call);
            var sinceNotes = 0;
            foreach (var window in job.Windows.Where(x => x.Status == "pending" && x.Attempts < MaxAttempts))
            {
                ct.ThrowIfCancellationRequested();
                if ((await repository.Latest(job.SessionId))?.CancelRequested == true) { job.CancelRequested = true; break; }
                if (await Window(job, window, ct) && job.Notes == "incremental" && ++sinceNotes >= NotesEveryWindows)
                { sinceNotes = 0; await Notes(job, rebuildAll: false, ct, final: false); }
                job.UpdatedAt = Now; job.Summary = Progress(job);
                await repository.Save(job);
            }
            if (job.CancelRequested)
            {
                job.Status = "cancelled";
                if (job.Notes == "incremental" && job.Windows.Any(x => x.Status == "completed")) await Notes(job, rebuildAll: false, ct, final: true);
                else job.NotesStatus = "skipped";
            }
            else
            {
                if (job.Windows.Any(x => x.Status == "pending" && x.Attempts < MaxAttempts))
                { job.Summary = Progress(job); await repository.Save(job); return; } // Retried on the next worker pass.
                if (job.Notes != "none") await Notes(job, job.Notes == "rebuild", ct, final: true);
                var failed = job.Windows.Count(x => x.Status == "failed" || x.Status == "pending");
                job.Status = job.Asr && failed == job.Windows.Count || job.NotesStatus == "failed" ? "failed" : "completed";
            }
            job.CompletedAt = Now; job.UpdatedAt = Now; job.Summary = Progress(job);
            await repository.Save(job);
            await activity.End(call, job.Status == "failed" ? "failed" : "completed", job.Summary);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { await repository.Save(job); await activity.End(call, "interrupted", "Paused by shutdown; resumes at the next pending window"); throw; }
        catch (Exception ex)
        {
            job.Status = "failed"; job.Error = AiActivity.SafeError(ex); job.CompletedAt = Now; job.UpdatedAt = Now;
            await repository.Save(job); await activity.Fail(call, ex);
        }
    }

    // True when a segment superseded rows, which reopens note work for it.
    async Task<bool> Window(RerunJob job, RerunWindow window, CancellationToken ct)
    {
        window.Attempts++;
        var recognition = await activity.Begin(job.SessionId, "ASR rerun window", asr.Model.Provider, job.AsrModel ?? asr.Model.Model, window.ReplacesIds);
        activity.Context(recognition, "session-rerun-v1", "One rerun window: consecutive saved chunks recognized together; no lecture-note prompt.",
            $"{window.Id} {window.StartMs}-{window.EndMs} ms, {window.SpeechChunkIds.Count} speech chunks");
        try
        {
            await activity.Start(recognition);
            var rows = (await repository.Transcripts(job.SessionId)).ToDictionary(x => x.Id, StringComparer.Ordinal);
            var replaced = window.ReplacesIds.Select(id => rows.GetValueOrDefault(id)).ToList();
            if (replaced.Any(x => x is null || !x.Active)) throw new InvalidOperationException("Transcript changed since the rerun was planned; first-pass text retained");
            if (replaced.Any(x => x!.CoveredChunkIds.Any(id => !window.SpeechChunkIds.Contains(id))))
                throw new InvalidOperationException("Window would split an earlier segment; first-pass text retained");
            var path = await windows.Combine(job.SessionId, window, ct);
            using var speech = await audio.Prepare(path, ct);
            if (!speech.HasSpeech)
            {
                window.Status = "skipped"; window.Error = "No speech found; first-pass transcript retained";
                await activity.End(recognition, "completed", window.Error); return false;
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = await asr.Transcribe(new(speech.Path, job.SessionId, window.SourceId, window.StartMs, window.AudioId,
                job.AsrLanguage, job.AsrModel, Pass: "rerun-" + job.Id), ct);
            recognition.UsageJson = result.UsageJson; recognition.ProviderLatencyMs = watch.ElapsedMilliseconds;
            if (string.IsNullOrWhiteSpace(result.Text)) throw new InvalidOperationException("Rerun returned no words; first-pass transcript retained");
            window.Model = result.Model?.Model;
            var firstPass = string.Join(" ", replaced.Select(x => x!.SourceText).Where(x => !string.IsNullOrWhiteSpace(x)));
            var display = audio.Display(result.Text, job.AsrLanguage, result.Language);
            if (display == firstPass || result.Text == firstPass)
            {
                window.Status = "unchanged";
                await activity.End(recognition, "completed", "Same wording as the first pass; nothing superseded", result.Model?.Model); return false;
            }
            var segment = Segment(job, window, replaced!, result, display, firstPass);
            if (!await apply(job.SessionId, () => repository.Supersede(window, segment), ct))
                throw new InvalidOperationException("Transcript changed during the rerun; first-pass text retained");
            window.Status = "completed"; window.Error = null;
            await activity.End(recognition, "completed", $"Segment saved; {replaced.Count} rows superseded" +
                (result.FallbackSummary is null ? "" : "; " + result.FallbackSummary), result.Model?.Model);
            return true;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            window.Error = AiActivity.SafeError(ex);
            if (window.Attempts >= MaxAttempts) window.Status = "failed";
            await activity.Fail(recognition, ex);
            return false;
        }
        catch (Exception ex) { await activity.Fail(recognition, ex); window.Attempts--; throw; }
    }

    Transcript Segment(RerunJob job, RerunWindow window, List<Transcript> replaced, AsrResult result, string display, string firstPass)
    {
        var uncertain = Transcript.NeedsReview(result.Text);
        var suppressed = replaced.Count > 0 && replaced.All(x => x.NoteStatus == "suppressed");
        return new Transcript
        {
            Id = window.Id, SessionId = job.SessionId, SourceId = window.SourceId, Kind = "window", RunId = job.Id,
            ChunkIds = window.SpeechChunkIds.ToList(), StartMs = window.StartMs, EndMs = window.EndMs, RecordedAt = window.RecordedAt,
            Original = result.Text, DisplayOriginal = display == result.Text ? null : display, FirstPassOriginal = firstPass,
            ReviewStatus = "completed", ReviewedAt = Now, ConfirmedAt = Now,
            AsrLanguageHint = result.Model?.SupportsLanguageHint == true ? AsrLanguage.ProviderHint(job.AsrLanguage) : null,
            AsrDetectedLanguage = result.Language, AsrProvider = result.Model?.Provider, AsrModel = result.Model?.Model,
            Uncertain = uncertain, RecognitionStatus = uncertain ? "review-needed" : "recognized",
            NoteStatus = suppressed ? "suppressed" : "pending", TranslationStatus = "pending"
        };
    }

    async Task Notes(RerunJob job, bool rebuildAll, CancellationToken ct, bool final)
    {
        job.NotesStatus = "running"; await repository.Save(job);
        try
        {
            if (rebuildAll) await rebuild(job.SessionId, ct); else await notes(job.SessionId, ct);
            job.NotesStatus = final ? "completed" : "running";
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Superseded rows stay pending, so the normal note scheduler can still write them later.
            job.NotesStatus = "failed"; job.Error = "Notes: " + AiActivity.SafeError(ex);
        }
    }

    static string Progress(RerunJob job)
    {
        if (!job.Asr) return $"Notes {job.NotesStatus}";
        int Count(string status) => job.Windows.Count(x => x.Status == status);
        return $"{Count("completed")} of {job.Windows.Count} windows re-transcribed, {Count("unchanged")} unchanged, {Count("skipped")} without speech, " +
            $"{Count("failed")} failed (first pass kept); notes {job.NotesStatus}";
    }
}
