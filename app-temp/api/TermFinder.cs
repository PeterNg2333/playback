using System.Text.RegularExpressions;

public sealed record TermCandidate(string Text, List<string> TranscriptIds, List<string> MaterialIds);

public static class TermFinder
{
    // Only label terms whose exact wording appears in a session source. Emphasis/headings in
    // materials and acronyms/compound names in recognized speech provide conservative candidates.
    static readonly Regex MarkedMaterial = new(@"(?:\*\*|__)([^\r\n*]{3,80})(?:\*\*|__)|^#{1,4}\s+([^\r\n]{3,80})", RegexOptions.Multiline | RegexOptions.Compiled);
    static readonly Regex SpeechTerm = new(@"\b[A-Z]{2,8}\b|\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)+\b", RegexOptions.Compiled);

    public static List<TermCandidate> Find(IEnumerable<Material> materials, IEnumerable<Transcript> transcripts)
    {
        var materialList = materials.ToList();
        var transcriptList = transcripts.Where(x => x.RecognitionStatus != "asr-empty" && !x.Uncertain).ToList();
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var material in materialList)
            foreach (Match match in MarkedMaterial.Matches(material.Text))
                candidates.Add((match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).Trim());
        foreach (var transcript in transcriptList)
            foreach (Match match in SpeechTerm.Matches(Regex.Replace(transcript.Original, @"<\|[^|>]*\|>", "")))
                candidates.Add(match.Value);
        return candidates.Where(x => x.Length is >= 3 and <= 80)
            .Select(term => new TermCandidate(term,
                transcriptList.Where(x => x.Original.Contains(term, StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).ToList(),
                materialList.Where(x => x.Text.Contains(term, StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).ToList()))
            .Where(x => x.TranscriptIds.Count + x.MaterialIds.Count > 0)
            .OrderBy(x => x.Text, StringComparer.OrdinalIgnoreCase).Take(100).ToList();
    }
}
