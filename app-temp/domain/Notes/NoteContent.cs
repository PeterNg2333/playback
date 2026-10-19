using System.Text.RegularExpressions;

namespace Playback.Domain.Notes;

// Rules every AI-written note text must meet, whichever path wrote it.
public static class NoteContent
{
    const int MinimumMeaningfulCharacters = 3;
    const int MaxFindLength = 2_000;
    const int MaxReplacementLength = 8_000;

    // "...", "mermaid..." or a bare citation is not a note, yet its citation would count as coverage.
    public static bool IsPlaceholder(string body)
    {
        var text = Regex.Replace(CitationSyntax.Rewrite(body, _ => ""), @"\[\s*\]", "").Trim();
        return Regex.IsMatch(text, @"^[A-Za-z]*\s*(?:\.{2,}|…+)$") || text.Count(char.IsLetterOrDigit) < MinimumMeaningfulCharacters;
    }

    // Replaces text that occurs exactly once in the block, like an editor's search-and-replace. Citations may not
    // be dropped, and coverage anchors shift with the edit; an edit that cuts across an anchor boundary is refused.
    public static void ReplaceOnce(NoteBlock block, string find, string replacement)
    {
        if (find.Length is < 1 or > MaxFindLength || replacement.Length > MaxReplacementLength)
            throw new InvalidOperationException("Invalid text edit size");
        var at = block.Body.IndexOf(find, StringComparison.Ordinal);
        if (at < 0 || block.Body.IndexOf(find, at + 1, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Edited text must occur exactly once in the block");
        if (CitationSyntax.Bodies(find).Except(CitationSyntax.Bodies(replacement)).Any())
            throw new InvalidOperationException("A text edit may not drop a citation");
        var end = at + find.Length;
        var delta = replacement.Length - find.Length;
        foreach (var anchor in block.Anchors)
        {
            var anchorEnd = anchor.Offset + anchor.Length;
            if (anchorEnd <= at) continue;
            if (anchor.Offset >= end) anchor.Offset += delta;
            else if (anchor.Offset <= at && anchorEnd >= end) anchor.Length += delta;
            else throw new InvalidOperationException("A text edit may not cross a coverage boundary");
        }
        var body = block.Body[..at] + replacement + block.Body[end..];
        if (IsPlaceholder(body)) throw new InvalidOperationException("A text edit may not empty a block");
        block.Body = body;
        block.CitationIds = CitationSyntax.Bodies(body).Distinct().ToList();
    }
}
