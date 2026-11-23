using System.Text.RegularExpressions;

namespace Playback.Domain.Audio;

// Names and terms from a session's materials, given to speech recognition as expected vocabulary so it spells people,
// products and jargon the way the materials do. Candidates are what the materials mark or set apart: headings, bold
// phrases and table cells, proper nouns, acronyms and codes, and short Chinese names in tables or after a colon. The
// most frequent come first; a recognizer reads only a short prompt.
public static partial class AsrVocabulary
{
    public const int MaxTerms = 60;
    public const int MaxPromptChars = 700;

    [GeneratedRegex(@"\*\*([^*\r\n]{2,60})\*\*")] private static partial Regex Bold();
    [GeneratedRegex(@"^#{1,4}\s+(.{2,80})$", RegexOptions.Multiline)] private static partial Regex Heading();
    [GeneratedRegex(@"^\|(.+)\|\s*$", RegexOptions.Multiline)] private static partial Regex TableRow();
    // "Peter Chan", "Smart Itinerary", "Cloud Run": capitalized words in a row.
    [GeneratedRegex(@"(?<![\p{L}\p{N}])[A-Z][a-zA-Z0-9&'.-]*[a-z0-9](?:[ \t]+(?:of[ \t]+|and[ \t]+|&[ \t]+)?[A-Z][a-zA-Z0-9&'.-]*[a-z0-9]){0,3}(?![\p{L}\p{N}])")] private static partial Regex ProperNoun();
    // "HKTB", "CR-1024", "iPhone", "GPT-5": acronyms, codes and mixed case.
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:[A-Z]{2,}[A-Z0-9]*(?:-[A-Z0-9]+)*|[A-Z]{2,}-\d+|[a-z]+[A-Z][a-zA-Z0-9]*|[A-Z][a-z]*\d+[a-zA-Z0-9]*)(?![\p{L}\p{N}])")] private static partial Regex Code();
    // A short Chinese phrase standing alone: a table cell, a line of its own, or after a colon.
    [GeneratedRegex(@"(?:^|[：:]\s*)([㐀-鿿]{2,8})\s*$", RegexOptions.Multiline)] private static partial Regex ChinesePhrase();

    static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "The", "This", "That", "These", "Those", "There", "Then", "When", "What", "Which", "Where", "Who", "Why", "How",
        "And", "But", "For", "With", "From", "Into", "About", "After", "Before", "Please", "Thank", "Thanks", "Yes", "No",
        "Page", "Slide", "Sheet", "Speaker", "Notes", "Note", "Meeting", "Minutes", "Agenda", "Date", "Time", "Venue",
        "Attendees", "Attendee", "Name", "Owner", "Task", "Action", "Actions", "Items", "Item", "Status", "Due", "Remarks",
        "Summary", "Sheet1", "Actions", "Mention", "Launch", "Review", "Discussion", "Decision", "Decisions", "Question", "Questions", "Answer", "Open", "Closed", "Done", "TBC", "TBD", "N/A",
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday", "January", "February", "March",
        "April", "May", "June", "July", "August", "September", "October", "November", "December", "AM", "PM", "OK",
        "出席者", "日期", "時間", "地點", "議程", "會議", "備註", "狀態", "負責", "負責人", "事項", "期限", "內容", "項目"
    };

    public static List<string> From(IEnumerable<string> texts)
    {
        var counts = new Dictionary<string, (string Term, int Score)>(StringComparer.OrdinalIgnoreCase);
        void Add(string raw, int weight)
        {
            var term = Regex.Replace(raw.Replace("**", ""), @"\s+", " ").Trim(' ', '.', ',', ':', '：', '-', '(', ')', '"', '\'');
            if (term.Length is < 2 or > 40 || Common.Contains(term) || term.All(c => char.IsDigit(c) || char.IsPunctuation(c))) return;
            if (term.Split(' ').Length > 4) return;
            counts[term] = counts.TryGetValue(term, out var known) ? (known.Term, known.Score + weight) : (term, weight);
        }
        foreach (var text in texts)
        {
            foreach (Match m in Bold().Matches(text)) Add(m.Groups[1].Value, 3);
            foreach (Match m in Heading().Matches(text))
                foreach (Match name in ProperNoun().Matches(m.Groups[1].Value)) Add(name.Value, 2);
            foreach (Match row in TableRow().Matches(text))
                foreach (var cell in row.Groups[1].Value.Split('|'))
                {
                    var value = cell.Trim();
                    if (Regex.IsMatch(value, @"^[㐀-鿿]{2,8}$")) Add(value, 2);
                    else if (value.Length <= 30 && ProperNoun().IsMatch(value) && ProperNoun().Match(value).Value == value) Add(value, 2);
                }
            var plain = text.Replace("**", "");
            foreach (Match m in ProperNoun().Matches(plain))
            {
                // A single capitalized word that opens a line or a sentence is usually an ordinary word ("Send", "Review").
                var lineStart = plain.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
                var before = plain[lineStart..m.Index];
                if (!m.Value.Contains(' ') && (Regex.IsMatch(before, @"^[\s\-#|*>•\d.)]*$") || Regex.IsMatch(before, @"[.!?:：。|]\s*$"))) continue;
                Add(m.Value, 1);
            }
            foreach (Match m in Code().Matches(plain)) Add(m.Value, 1);
            foreach (Match m in ChinesePhrase().Matches(plain)) Add(m.Groups[1].Value, 1);
        }
        // A one-word name inside a longer one already listed adds nothing.
        var ranked = counts.Values.OrderByDescending(x => x.Score).ThenBy(x => x.Term, StringComparer.Ordinal).Select(x => x.Term).ToList();
        var result = new List<string>();
        var length = 0;
        foreach (var term in ranked)
        {
            if (result.Count >= MaxTerms || length + term.Length + 2 > MaxPromptChars) break;
            if (result.Any(x => x.Contains(term, StringComparison.OrdinalIgnoreCase) && x.Length > term.Length && !term.Contains(' ') && term.Length < 4)) continue;
            result.Add(term);
            length += term.Length + 2;
        }
        return result;
    }

    public static string Prompt(IReadOnlyList<string> terms) => terms.Count == 0 ? "" : "Expected vocabulary: " + string.Join(", ", terms);
}
