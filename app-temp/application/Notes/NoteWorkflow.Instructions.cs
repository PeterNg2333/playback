using System.Diagnostics;
using Playback.Domain;
using Playback.Domain.Notes;

namespace Playback.Application.Notes;

// The note owner's own instruction ("turn this into a table", "merge these two", "the API name is NtQueryFile"),
// given for the whole notes, one topic or one block (a comment on that block). The reply is edit operations
// applied directly, because a person asked for them; history keeps the previous version for undo. Locked
// (protected) blocks stay untouched, and new content still needs lecture sources.
public sealed partial class NoteWorkflow
{
    const int MaxInstructionLength = 2_000;

    public sealed record InstructionResult(object Note, int Applied, List<string> NotApplied);

    public async Task<InstructionResult> Instruct(string id, string instruction, string? topicId, string? blockId, int baseVersion, CancellationToken ct)
    {
        instruction = instruction.Trim();
        if (instruction.Length is < 1 or > MaxInstructionLength) throw new InvalidOperationException("Write an instruction of up to 2,000 characters");
        if (!writer.IsConfigured) throw new InvalidOperationException("The note model is not configured; no instruction was sent");
        var sessionLock = SessionLock(id);
        await sessionLock.WaitAsync(ct);
        try
        {
            var session = await evidence.Session(id) ?? throw new InvalidOperationException("Session not found");
            if (session.NoteVersion != baseVersion) throw new InvalidOperationException("Notes changed; reload and give the instruction again");
            if (blockId is not null) topicId = session.Sections.FirstOrDefault(x => NoteTree.Canonical(x).Blocks.Any(b => b.Id == blockId))?.Id
                ?? throw new InvalidOperationException("Block not found");
            var context = topicId is null ? session.Sections.Where(x => !x.UserEdited).ToList()
                : [session.Sections.SingleOrDefault(x => x.Id == topicId) ?? throw new InvalidOperationException("Topic not found")];
            if (context.Count == 0) throw new InvalidOperationException("There are no unlocked notes to revise");
            var target = blockId is not null ? $"Target: block {blockId} in topic {topicId}." : topicId is not null ? $"Target: topic {topicId}." : "Target: the notes shown.";
            var instructions = NoteInstructions.For(session.NoteLanguage) + "\n" + NoteInstructions.Instruction + "\n" + target +
                "\nInstruction from the note owner:\n" + instruction;
            var input = NoteInput.Build(session, [], context, [], "instruction");
            var call = await activity.Begin(id, "Note instruction", writer.Provider, writer.Model,
                context.SelectMany(x => x.Points).SelectMany(x => x.SourceIds), session.NoteVersion);
            call.SectionId = topicId;
            activity.Context(call, NoteInstructions.Version, instructions, input.Text);
            await generationSlots.WaitAsync(ct);
            try
            {
                await activity.Start(call);
                var watch = Stopwatch.StartNew();
                NotePatch patch;
                try
                {
                    patch = await writer.Generate("LectureNoteInstruction", instructions, input.Text, ct,
                        onUpdate: text => activity.Draft(call, input.Decode(ReadableDraft(text))), onUsage: usage => call.UsageJson = usage);
                }
                finally { call.ProviderLatencyMs = watch.ElapsedMilliseconds; }
                var allowed = session.AllowedSources.ToList();
                var notApplied = new List<RejectedOperation>();
                if (patch.Sections.Count > 0) notApplied.Add(new(new NoteTreeOperation { Kind = "sections" }, "Whole-section replies are not applied; use operations"));
                patch.Sections.Clear(); patch.Deferred.Clear(); patch.Rewrites.Clear();
                ExpandPlacements(patch);
                SplitTopicContent(patch);
                InScript(patch, session.NoteLanguage);
                DecodePatch(patch, input, context, session, allowed);
                RejectUnknownReferences(session, patch, allowed, notApplied);
                var hash = ContentHash.Of("instruction:" + session.NoteVersion + instruction + input.Text);
                var baseNote = session.CurrentNote ?? new Note { SessionId = session.Id };
                baseNote.Citations = NoteSections.CloneCitations(baseNote.Citations);
                foreach (var section in context) NoteSections.Compact(session.Id, section.Markdown, allowed, baseNote.Citations);
                var update = NoteSections.Apply(session.Id, baseNote, session.Sections, patch, context.Select(x => x.Id), [], allowed, hash);
                var (operated, rejected) = NoteTree.ApplyEach(update.Sections, patch.Operations, allowed, hash);
                notApplied.AddRange(rejected);
                var applied = patch.Operations.Where(x => rejected.All(r => r.Operation != x)).ToList();
                var reasons = notApplied.Select(x => x.Reason).Distinct().ToList();
                if (applied.Count == 0)
                {
                    await activity.End(call, "completed", "No change applied" + (reasons.Count == 0 ? "" : ": " + string.Join("; ", reasons.Take(3))));
                    return new(new { Version = session.NoteVersion, Markdown = session.NoteMarkdown }, 0, reasons);
                }
                KeepLectureOrder(operated, update.Sections.Select(x => x.Id).ToHashSet(), session);
                var edited = applied.Where(x => x.Kind is "replace-text" or "delete-block").Select(x => x.BlockId).ToList();
                var policy = processingPolicy is null ? ProcessingPolicy.Defaults : (await processingPolicy.Effective(session.Id)).Values;
                var saved = await repository.Save(session.Id, new SectionUpdate(operated, update.Citations, [], policy.NotePresentation, edited), hash,
                    session.NoteVersion, session.NoteLanguage, author: "user instruction");
                await activity.End(call, "completed", $"{applied.Count} changes applied as instructed" +
                    (reasons.Count == 0 ? "" : $"; not applied: {string.Join("; ", reasons.Take(3))}"));
                return new(saved, applied.Count, reasons);
            }
            catch (Exception ex) { await activity.Fail(call, ex); throw; }
            finally { generationSlots.Release(); }
        }
        finally { sessionLock.Release(); }
    }
}
