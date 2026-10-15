using System.Text.RegularExpressions;

namespace Playback.Domain.Notes;

// Shared citation boundaries: fenced/inline code and Markdown links are literal content.
public static class CitationSyntax
{
    static readonly Regex Brackets = new(@"(?<code>```[\s\S]*?```|~~~[\s\S]*?~~~|`[^`\r\n]*`)|\[(?<body>[^\[\]\r\n]{1,16000})\](?!\()", RegexOptions.Compiled);
    public static IEnumerable<string> Bodies(string text) => Brackets.Matches(text)
        .Where(match => !match.Groups["code"].Success).Select(match => match.Groups["body"].Value);
    public static string Rewrite(string text, Func<string, string> rewrite) => Brackets.Replace(text, match =>
        match.Groups["code"].Success ? match.Value : "[" + rewrite(match.Groups["body"].Value) + "]");
}
