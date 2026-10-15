using Playback.Domain.Notes;
using System.Text.RegularExpressions;

namespace Playback.Application.Notes;

public sealed record NoteRewritePending(int Version, string Markdown, List<string> ProposalIds);
public sealed partial class NoteWorkflow
{
    List<NoteRewriteProposal> RewriteCandidates(NoteSession session, NotePatch patch)
    {
        if (session.CurrentNote is null) return [];
        var result = new List<NoteRewriteProposal>();
        foreach (var replacement in patch.Sections.Where(x => x.Id != "new"))
        {
            var topic = session.Sections.SingleOrDefault(x => x.Id == replacement.Id);
            if (topic is null) continue;
            var canonical = NoteTree.Canonical(topic);
            var citations = NoteSections.CloneCitations(session.CurrentNote.Citations);
            string Normalize(string body) => NoteSections.Compact(session.Id, NoteSections.Expand(body, citations), session.AllowedSources, citations).Replace("\r\n", "\n");
            var body = Regex.Replace(replacement.Markdown, @"\A#{1,4}\s+[^\r\n]+\r?\n\s*\n?", "");
            if (canonical.Blocks.All(block => Normalize(body).Contains(Normalize(block.Body), StringComparison.Ordinal))) continue;
            var retainedPoints = replacement.Points.SelectMany(x => x.Retains).ToHashSet();
            var mapped = canonical.Blocks.Where(x => x.Anchors.All(anchor => retainedPoints.Contains(anchor.Id))).Select(x => x.Id).ToList();
            result.Add(NoteRewrite.Preview(session.CurrentNote, topic.Id, [new() {TargetBlockId = canonical.Blocks[0].Id, RetainedBlockIds = mapped, Body = body, Role = "legacy"}], session.AllowedSources, clock.GetUtcNow().UtcDateTime));
        }
        foreach (var group in patch.Rewrites.GroupBy(change => session.Sections.FirstOrDefault(x => NoteTree.Canonical(x).Blocks.Any(block => block.Id == change.TargetBlockId))?.Id ?? throw new InvalidOperationException("Unknown rewrite target")))
            result.Add(NoteRewrite.Preview(session.CurrentNote, group.Key, group.ToList(), session.AllowedSources, clock.GetUtcNow().UtcDateTime));
        return result;
    }
    public async Task<NoteRewriteProposal> PreviewRewrite(string id, string sectionId, int baseVersion, CancellationToken ct)
    {
        if (rewrites is null) throw new InvalidOperationException("Rewrite previews are unavailable");
        var sessionLock = SessionLock(id); await sessionLock.WaitAsync(ct);
        try
        {
            var session = await evidence.Session(id) ?? throw new InvalidOperationException("Session not found");
            if (session.NoteVersion != baseVersion || session.CurrentNote is null) throw new InvalidOperationException("Notes changed; reload before previewing a rewrite");
            var topic = session.Sections.SingleOrDefault(x => x.Id == sectionId) ?? throw new InvalidOperationException("Topic not found");
            var canonical = NoteTree.Canonical(topic);
            var input = NoteInput.Build(session, [], [canonical], [], "rewrite-preview");
            var instructions = NoteInstructions.For(session.NoteLanguage) + "\n" + NoteInstructions.Rewrite;
            if (!gemini.IsConfigured) throw new InvalidOperationException("Gemini is not configured; no rewrite preview was generated");
            var call = await activity.Begin(id, "Note rewrite preview", "vertex", gemini.Model, canonical.Points.SelectMany(x => x.SourceIds), baseVersion);
            activity.Context(call, "retained-block-rewrite-v1", instructions, input.Text);
            await generationSlots.WaitAsync(ct);
            try
            {
                await activity.Start(call);
                var patch = await gemini.Generate("LectureBlockRewrite", instructions, input.Text, ct, onUpdate: text => activity.Draft(call, input.Decode(ReadableDraft(text))), onUsage: usage => call.UsageJson = usage);
                foreach (var change in patch.Rewrites) change.Body = input.Decode(change.Body);
                var proposal = NoteRewrite.Preview(session.CurrentNote, sectionId, patch.Rewrites, session.AllowedSources, clock.GetUtcNow().UtcDateTime);
                await rewrites.SaveRewrite(proposal);
                await activity.End(call, "review-required", proposal.ReadyForAcceptance ? "Preview ready for semantic review; notes unchanged" : "Retention checks blocked this preview; prior bodies unchanged");
                return proposal;
            }
            catch (Exception ex) { await activity.Fail(call, ex); throw; }
            finally { generationSlots.Release(); }
        }
        finally { sessionLock.Release(); }
    }
}
