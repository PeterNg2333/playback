using System.Text.RegularExpressions;

namespace Playback.Domain.Notes;

// Rules every AI-written note text must meet, whichever path wrote it.
public static class NoteContent
{
    const int MinimumMeaningfulCharacters = 3;

    // "...", "mermaid..." or a bare citation is not a note, yet it would look like the source was written up.
    public static bool IsPlaceholder(string body)
    {
        var text = Regex.Replace(CitationSyntax.Rewrite(body, _ => ""), @"\[\s*\]", "").Trim();
        return Regex.IsMatch(text, @"^[A-Za-z]*\s*(?:\.{2,}|…+)$") || text.Count(char.IsLetterOrDigit) < MinimumMeaningfulCharacters;
    }
}
