using Playback.Api.Db;

namespace Playback.Api.Services.Ai;

// Observable citation coverage, never a semantic-completeness score. Completed metadata
// and a coverage ledger cannot substitute for a citation in the saved document body.
public static class NoteCoverage
{
    public const long LargeGapMs = 120_000;
    public static HashSet<string> Referenced(SessionView session) => NoteSections.Sources(
        NoteSections.Expand(session.NoteMarkdown, session.CurrentNote?.Citations ?? []),
        session.Transcripts.Select(x => x.Id)).ToHashSet(StringComparer.Ordinal);
    public static bool Suppressed(SessionView session, Transcript source) => source.NoteStatus == "suppressed" ||
        (session.CurrentNote?.SuppressedSourceIds.Contains(source.Id) ?? false);
    public static NoteCoverageReport Audit(SessionView session) {
        var referenced = Referenced(session);
        var sources = session.Transcripts.Where(x => !string.IsNullOrWhiteSpace(x.SourceText)).ToList();
        var missing = sources.Where(x => !referenced.Contains(x.Id) && !Suppressed(session, x)).ToList();
        var missingIds = missing.Select(x => x.Id).ToHashSet();
        var gaps = new List<NoteCoverageGap>();
        foreach (var track in sources.GroupBy(x => x.SourceId)) {
            var run = new List<Transcript>();
            void Flush() {
                if (run.Count == 0) return;
                long duration = 0, through = run[0].StartMs;
                foreach (var source in run) {
                    duration += Math.Max(0, source.EndMs - Math.Max(through, source.StartMs));
                    through = Math.Max(through, source.EndMs);
                }
                gaps.Add(new(track.Key, run[0].StartMs, through, duration,
                    run.Select(x => x.Id).ToList(), run.Count(x => x.NoteStatus == "deferred"),
                    run.Count(x => x.NoteStatus == "completed"), duration >= LargeGapMs));
                run.Clear();
            }
            foreach (var source in track.OrderBy(x => x.StartMs).ThenBy(x => x.Id)) {
                if (!missingIds.Contains(source.Id)) { Flush(); continue; }
                // Silence between passages is not unreferenced speech.
                if (run.Count > 0 && source.StartMs - run.Max(x => x.EndMs) > 30_000) Flush();
                run.Add(source);
            }
            Flush();
        }
        gaps = gaps.OrderBy(x => x.StartMs).ThenBy(x => x.SourceId).ToList();
        return new(session.NoteVersion, sources.Count, sources.Count(x => referenced.Contains(x.Id)),
            sources.Count(x => Suppressed(session, x) && !referenced.Contains(x.Id)), missing.Count,
            missing.Count(x => x.NoteStatus == "completed"), gaps.Count(x => x.Large), LargeGapMs, gaps);
    }
    // Explicit gap repair visits the earliest hole, including old completed and deferred
    // sources. One click/call admits one bounded batch; it never resets a whole session.
    public static List<Transcript> OldestBatch(SessionView session) {
        var gap = Audit(session).Gaps.FirstOrDefault();
        if (gap is null) return [];
        var ids = gap.SourceIds.ToHashSet(); var result = new List<Transcript>(); var chars = 0;
        foreach (var source in session.Transcripts.Where(x => ids.Contains(x.Id)).OrderBy(x => x.StartMs).ThenBy(x => x.Id)) {
            if (result.Count >= 32 || result.Count > 0 && chars + source.SourceText.Length > 9000) break;
            result.Add(source); chars += source.SourceText.Length;
        }
        return result;
    }
}
public sealed record NoteCoverageGap(string SourceId, long StartMs, long EndMs, long SpeechMs,
    List<string> SourceIds, int Deferred, int CompletedWithoutReference, bool Large);
public sealed record NoteCoverageReport(int Version, int Total, int Referenced, int Suppressed, int Unreferenced,
    int CompletedWithoutReference, int LargeGaps, long LargeGapMs, List<NoteCoverageGap> Gaps);
