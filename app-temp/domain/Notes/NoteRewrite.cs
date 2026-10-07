using System.Text.Json;
using System.Text.RegularExpressions;

namespace Playback.Domain.Notes;

public sealed class RewriteBlockChange
{
    public string TargetBlockId { get; set; } = "";
    public List<string> RetainedBlockIds { get; set; } = [];
    public string Body { get; set; } = "";
    public string Role { get; set; } = "detail";
}
public sealed class NoteRewriteProposal
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public int BasedOnVersion { get; set; }
    public string TopicId { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string Status { get; set; } = "review-required";
    public List<RewriteBlockChange> Changes { get; set; } = [];
    public Dictionary<string, string> OriginalBodies { get; set; } = [];
    public List<string> Issues { get; set; } = [];
    public bool ReadyForAcceptance { get; set; }
}
public static class NoteRewrite
{
    public static NoteRewriteProposal Preview(Note note, string topicId, List<RewriteBlockChange> changes, IEnumerable<string> allowedSources, DateTime now)
    {
        var proposal = new NoteRewriteProposal {Id = "rewrite_" + ContentHash.Of(note.SessionId + note.Version + topicId + JsonSerializer.Serialize(changes)),
            SessionId = note.SessionId, BasedOnVersion = note.Version, TopicId = topicId, CreatedAt = now, Changes = changes};
        var topic = note.Sections.SingleOrDefault(x => x.Id == topicId) ?? throw new InvalidOperationException("Rewrite topic not found");
        var blocks = NoteTree.Canonical(topic).Blocks;
        proposal.OriginalBodies = blocks.ToDictionary(x => x.Id, x => x.Body);
        var used = new HashSet<string>(); var allowed = allowedSources.ToHashSet();
        if (changes.Count is < 1 or > 4) proposal.Issues.Add("Select one to four rewrite/merge changes");
        foreach (var change in changes)
        {
            var retained = blocks.Where(x => change.RetainedBlockIds.Contains(x.Id)).OrderBy(x => x.Order).ToList();
            if (change.RetainedBlockIds.Count is < 1 or > 10 || retained.Count != change.RetainedBlockIds.Distinct().Count() || !retained.Any(x => x.Id == change.TargetBlockId) || change.Body.Trim().Length is < 1 or > 32000)
            { proposal.Issues.Add("Unknown/invalid retained-block mapping or body size"); continue; }
            if (retained.Any(x => !used.Add(x.Id)) || retained[0].Id != change.TargetBlockId ||
                blocks.OrderBy(x => x.Order).SkipWhile(x => x.Id != retained[0].Id).Take(retained.Count).Any(x => !retained.Contains(x)))
                proposal.Issues.Add("Merge must map contiguous blocks once, preserving their original position");
            if (change.Role is not ("legacy" or "definition" or "example" or "derivation" or "correction" or "detail")) proposal.Issues.Add("Unknown block role");
            var expanded = NoteSections.Expand(change.Body, note.Citations);
            var cited = NoteSections.Sources(expanded, allowed);
            var originalSources = retained.SelectMany(x => x.Anchors).SelectMany(x => x.SourceIds).Distinct();
            if (originalSources.Except(cited).Any()) proposal.Issues.Add("Rewrite lost an existing source citation");
            try { NoteReferences.ValidateSourceReferences(expanded, allowed); }
            catch (InvalidOperationException) { proposal.Issues.Add("Rewrite introduced an unknown citation"); }
            foreach (var block in retained)
                foreach (var fragment in ProtectedFragments(NoteSections.Expand(block.Body, note.Citations)))
                    if (!expanded.Contains(fragment, StringComparison.Ordinal)) proposal.Issues.Add("Rewrite lost a formula, code, number, condition, uncertainty or example: " + block.Id);
        }
        proposal.Issues = proposal.Issues.Distinct().ToList(); proposal.ReadyForAcceptance = proposal.Issues.Count == 0;
        if (!proposal.ReadyForAcceptance) proposal.Status = "blocked-retention";
        return proposal;
    }
    static IEnumerable<string> ProtectedFragments(string body)
    {
        foreach (Match match in Regex.Matches(body, @"```[\s\S]*?```|~~~[\s\S]*?~~~|`[^`\r\n]+`|\$\$[\s\S]*?\$\$|(?<!\$)\$[^$\r\n]+\$|\\\([\s\S]*?\\\)|\\\[[\s\S]*?\\\]")) yield return match.Value;
        var withoutCitations = CitationSyntax.Rewrite(body, _ => "");
        foreach (Match match in Regex.Matches(withoutCitations, @"(?<![a-zA-Z0-9_])\d+(?:\.\d+)?(?:%|\b)")) yield return match.Value;
        foreach (var paragraph in Regex.Split(body, @"\r?\n\s*\r?\n"))
            if (Regex.IsMatch(paragraph, @"\b(?:only|if|unless|provided|assuming|except|when|under|may|might|uncertain|unclear|example)\b|如果|除非|條件|条件|假設|假设|不確定|不确定|例子|示例", RegexOptions.IgnoreCase)) yield return paragraph.Trim();
    }
    public static List<NoteSection> Accept(Note note, NoteRewriteProposal proposal, IEnumerable<string> allowedSources, bool semanticAccepted)
    {
        if (!semanticAccepted) throw new InvalidOperationException("Review and explicitly accept the semantic changes before applying");
        if (note.Version != proposal.BasedOnVersion) throw new InvalidOperationException("Notes changed; retain this proposal and create a fresh preview");
        var inspected = Preview(note, proposal.TopicId, proposal.Changes, allowedSources, proposal.CreatedAt);
        if (!inspected.ReadyForAcceptance || inspected.OriginalBodies.Any(x => !proposal.OriginalBodies.TryGetValue(x.Key, out var body) || body != x.Value)) throw new InvalidOperationException("Rewrite failed retention checks; prior bodies remain in place");
        var topics = note.Sections.Select(x => NoteTree.Canonical(x)).ToList(); var topic = topics.Single(x => x.Id == proposal.TopicId);
        foreach (var change in proposal.Changes)
        {
            var retained = topic.Blocks.Where(x => change.RetainedBlockIds.Contains(x.Id)).ToList(); var target = retained.Single(x => x.Id == change.TargetBlockId);
            var anchors = retained.SelectMany(x => x.Anchors).Select(x => new PointAnchor {Id = x.Id, Length = change.Body.Length, SourceIds = x.SourceIds.ToList()}).ToList();
            var newlyCited = NoteSections.Sources(NoteSections.Expand(change.Body, note.Citations), allowedSources).Except(anchors.SelectMany(x => x.SourceIds)).ToList();
            if (newlyCited.Count > 0) anchors.Add(new() {Id = "point_" + ContentHash.Of(target.Id + change.Body)[..20], Length = change.Body.Length, SourceIds = newlyCited});
            foreach (var merged in retained.Where(x => x != target)) topic.Blocks.Remove(merged);
            target.Body = change.Body; target.Anchors = anchors; target.Role = change.Role; target.UserEdited = true;
            target.RetainedBlockIds = retained.SelectMany(x => x.RetainedBlockIds).Concat(change.RetainedBlockIds).Distinct().ToList();
            target.CitationIds = CitationSyntax.Bodies(change.Body).Distinct().ToList();
        }
        topic.Version++; NoteTree.Validate(topics); return topics;
    }
}
