namespace Playback.Domain.Models;

// The compact job state the page shows; the window plan itself stays on the server.
public sealed record RerunView(string Id, string Status, bool Asr, string? AsrModel, int WindowSeconds, string Notes, string NotesStatus,
    int Windows, int Completed, int Unchanged, int Skipped, int Failed, int Pending, bool CancelRequested, string Summary, string? Error,
    DateTime CreatedAt, DateTime? CompletedAt)
{
    public static RerunView Of(RerunJob job)
    {
        int Count(string status) => job.Windows.Count(x => x.Status == status);
        return new(job.Id, job.Status, job.Asr, job.AsrModel, job.WindowSeconds, job.Notes, job.NotesStatus, job.Windows.Count,
            Count("completed"), Count("unchanged"), Count("skipped"), Count("failed"), Count("pending"), job.CancelRequested, job.Summary, job.Error,
            job.CreatedAt, job.CompletedAt);
    }
}
