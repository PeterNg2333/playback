using Playback.Api.Db;
using Coverage = Playback.Application.Notes.NoteCoverage;

namespace Playback.Api.Notes;

public static class NoteCoverage
{
    public const long LargeGapMs = Coverage.LargeGapMs;
    public static HashSet<string> Referenced(SessionView session) => Coverage.Referenced(NoteStoreAdapter.Read(session));
    public static bool Suppressed(SessionView session, Transcript source) => Coverage.Suppressed(NoteStoreAdapter.Read(session), NoteStoreAdapter.Read(source));
    public static NoteCoverageReport Audit(SessionView session) => Coverage.Audit(NoteStoreAdapter.Read(session));
    public static List<Transcript> OldestBatch(SessionView session) => Coverage.OldestBatch(NoteStoreAdapter.Read(session))
        .Select(x => session.Transcripts.First(source => source.Id == x.Id)).ToList();
}
