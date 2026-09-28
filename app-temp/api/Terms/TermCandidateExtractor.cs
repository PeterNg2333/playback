using Playback.Api.Db;
using System.Text.RegularExpressions;

namespace Playback.Api.Terms;

public sealed record TermCandidate(string Text, List<string> TranscriptIds, List<string> MaterialIds, string Context = "");

public static class TermCandidateExtractor
{
    // Only label terms whose exact wording appears in a session source. Emphasis/headings in
    // materials and acronyms/compound names in recognized speech provide conservative candidates.
    static readonly Regex MarkedMaterial = new(
        @"(?:\*\*|__)([^\r\n*]{3,80})(?:\*\*|__)|^#{1,4}\s+([^\r\n]{3,80})",
        RegexOptions.Multiline | RegexOptions.Compiled);
    static readonly Regex SpeechTerm = new(@"(?<![A-Za-z0-9])[A-Z]{2,8}(?![A-Za-z0-9])|\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)+\b", RegexOptions.Compiled);
    static readonly Regex ContextualTerm = new(
        @"(?:講|叫做|稱為|所謂|關於|关于|解釋|解释|討論|讨论)\s*(?:一吓|一下)?\s*([\u3400-\u9FFF]{2,10}|[A-Za-z][A-Za-z0-9-]{2,30})|\b([a-z][a-z0-9-]{2,30})\s+(?:is|means|stores|refers to)\b|(?<![A-Za-z0-9])(?:cache|spectrogram|latency|throughput|deadlock|semaphore|mutex)(?![A-Za-z0-9])|緩存|缓存|快取|傅立葉轉換|傅里叶变换|頻譜|频谱|卷積|卷积",
        RegexOptions.Compiled);

    public static List<TermCandidate> Find(IEnumerable<Material> materials, IEnumerable<Transcript> transcripts)
    {
        var materialList = materials.ToList();
        var transcriptList = transcripts.Where(x => x.RecognitionStatus != "asr-empty" && !x.Uncertain).ToList();
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var material in materialList)
            foreach (Match match in MarkedMaterial.Matches(material.Text))
                candidates.Add((match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).Trim());
        foreach (var transcript in transcriptList)
        {
            foreach (Match match in SpeechTerm.Matches(Regex.Replace(transcript.SourceText, @"<\|[^|>]*\|>", "")))
                candidates.Add(match.Value);
            foreach (Match match in ContextualTerm.Matches(transcript.SourceText))
                candidates.Add(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Success ? match.Groups[2].Value : match.Value);
        }
        return candidates.Where(x => x.Length is >= 2 and <= 80)
            .Select(term => new TermCandidate(term,
                transcriptList.Where(x => x.SourceText.Contains(term, StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).ToList(),
                materialList.Where(x => x.Text.Contains(term, StringComparison.OrdinalIgnoreCase)).Select(x => x.Id).ToList(),
                Context(term, transcriptList.LastOrDefault(x => x.SourceText.Contains(term, StringComparison.OrdinalIgnoreCase))?.SourceText
                    ?? materialList.FirstOrDefault(x => x.Text.Contains(term, StringComparison.OrdinalIgnoreCase))?.Text ?? "")))
            .Where(x => x.TranscriptIds.Count + x.MaterialIds.Count > 0)
            .OrderBy(x => x.Text, StringComparer.OrdinalIgnoreCase).Take(100).ToList();
    }
    static string Context(string term, string source)
    {
        var at = Math.Max(0, source.IndexOf(term, StringComparison.OrdinalIgnoreCase) - 80);
        return source.Substring(at, Math.Min(400, source.Length - at));
    }
}
