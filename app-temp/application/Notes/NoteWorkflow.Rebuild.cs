using Playback.Domain;
using Playback.Domain.Notes;

namespace Playback.Application.Notes;

public sealed record NoteRebuildResult(int StartVersion, int Version, int Rounds, int RemainingSources);
public sealed partial class NoteWorkflow
{
    // A rebuild writes the lecture again with the ordinary batched writer. The first new version keeps only locked
    // blocks (and topics the user protected) with their parent path; history keeps every earlier version.
    const int MaxRebuildRounds = 600;
    public async Task<NoteRebuildResult> Rebuild(string id, CancellationToken ct)
    {
        int start;
        var sessionLock = SessionLock(id);
        await sessionLock.WaitAsync(ct);
        try
        {
            var session = await evidence.Session(id) ?? throw new InvalidOperationException("Session not found");
            var note = session.CurrentNote;
            var suppressed = (note?.SuppressedSourceIds ?? []).Concat(note?.Coverage.Where(x => x.Status == "suppressed").Select(x => x.SourceId) ?? [])
                .ToHashSet(StringComparer.Ordinal);
            var sources = session.Transcripts.Where(x => !string.IsNullOrWhiteSpace(x.SourceText) && !suppressed.Contains(x.Id)).Select(x => x.Id).ToList();
            if (sources.Count == 0 && session.Materials.Count == 0) throw new InvalidOperationException("No transcript or material to write notes from");
            var kept = Locked(session.Sections.Select(x => NoteTree.Canonical(x)).ToList());
            // Explicit pending coverage replaces every earlier disposition, so prior "covered" entries are not
            // turned into user suppressions when their points leave the tree.
            var coverage = (note?.Coverage.Select(x => x.SourceId) ?? []).Concat(sources).Distinct(StringComparer.Ordinal)
                .Where(x => !suppressed.Contains(x))
                .Select(x => new SourceDisposition { SourceId = x, Status = "pending", Reason = "Notes rebuilt from the current transcript" }).ToList();
            start = session.NoteVersion;
            if (note is not null)
                await repository.Save(id, new SectionUpdate(kept, NoteSections.CloneCitations(note.Citations), coverage),
                    ContentHash.Of("rebuild:" + start + ":" + string.Join("|", sources)), start, session.NoteLanguage, "user rebuild");
            await evidence.MarkNotes(sources, "pending");
        }
        finally { sessionLock.Release(); }

        var rounds = await WritePending(id, ct);
        var final = await evidence.Session(id);
        return new(start, final?.NoteVersion ?? start, rounds, final is null ? 0 : Unwritten(final));
    }

    static int Unwritten(NoteSession session) =>
        NoteInput.Pending(session.Transcripts).Count == 0 ? 0 : session.Transcripts.Count(x => !string.IsNullOrWhiteSpace(x.SourceText) && x.NoteStatus is not ("completed" or "suppressed"));

    // Writes pending sources batch by batch until none remain or two rounds make no progress. Unlike Generate,
    // it never falls back to revising every section when nothing is pending.
    public async Task<int> WritePending(string id, CancellationToken ct)
    {
        var rounds = 0; var stalled = 0; var remaining = int.MaxValue;
        while (rounds < MaxRebuildRounds && stalled < 2)
        {
            ct.ThrowIfCancellationRequested();
            var session = await evidence.Session(id) ?? throw new InvalidOperationException("Session not found");
            var pending = NoteInput.Pending(session).Count == 0 ? 0 : Unwritten(session);
            if (pending == 0 && NoteInput.Materials(session.Materials, session.CurrentNote).Count == 0) break;
            stalled = pending >= remaining ? stalled + 1 : 0; remaining = pending;
            await Generate(id, ct); rounds++;
        }
        return rounds;
    }

    static List<NoteSection> Locked(List<NoteSection> topics)
    {
        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var topic in topics.Where(x => x.UserEdited || x.Blocks.Any(b => b.UserEdited)))
            for (var current = topic; current is not null && keep.Add(current.Id); current = topics.FirstOrDefault(x => x.Id == current.ParentId)) { }
        return topics.Where(x => keep.Contains(x.Id)).Select(topic =>
        {
            var copy = NoteTree.Canonical(topic);
            if (!topic.UserEdited) copy.Blocks = copy.Blocks.Where(x => x.UserEdited).ToList();
            return copy;
        }).ToList();
    }
}
