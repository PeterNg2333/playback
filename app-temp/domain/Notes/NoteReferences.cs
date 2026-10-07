using System.Text.RegularExpressions;

namespace Playback.Domain.Notes;

public static class NoteReferences
{
    public static void ValidateReferences(string markdown, IEnumerable<string> allowed)
    {
        var ids = allowed.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var body in CitationSyntax.Bodies(markdown).Where(x => x.StartsWith("ref:", StringComparison.OrdinalIgnoreCase)))
            if (!ids.Contains(body[4..]))
                throw new InvalidOperationException("AI note contains an unknown term reference");
    }

    public static void ValidateSourceReferences(string markdown, IEnumerable<string> allowed)
    {
        var ids = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var body in CitationSyntax.Bodies(markdown))
        {
            if (body.StartsWith("ref:", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (Match token in Regex.Matches(body, @"[A-Za-z0-9_-]+"))
                if (!ids.Contains(token.Value) && Regex.IsMatch(token.Value,
                    @"^(?:T\d+|cite_[a-z0-9_-]*|(?:[a-f0-9]{32,}|[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12})(?:[-_][a-z0-9_-]+)?)$", RegexOptions.IgnoreCase))
                    throw new InvalidOperationException("AI note contains an unknown source reference");
        }
    }

}
