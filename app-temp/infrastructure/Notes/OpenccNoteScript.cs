using System.Reflection;
using OpenccNetLib;
using Playback.Application.Notes;

namespace Playback.Api.Notes;

// Simplified text that slips into Traditional notes becomes Traditional (Hong Kong) with OpenCC's phrase-aware s2hk
// conversion, in two passes over the text as the model wrote it.
//
// Characters: a character that exists only in Simplified changes, and so does a shared character next to one, because
// then the phrase is Simplified and OpenCC's phrase conversion applies (这里 → 這裡). A shared character elsewhere (干, 后,
// 里, 了, 台, 着…) stays, because converting it alone would turn correct text such as 干擾 into 幹擾. OpenCC's own lists
// decide: a character is Simplified-only when it is not among its own Traditional candidates in STCharacters and is not
// itself a Traditional character in TSCharacters.
//
// Phrases: a word made only of shared characters (示范, 根据, 背后) is found the way OpenCC segments text, by the longest
// STPhrases entry at each position, and changes when all three hold: every character s2hk changes in it is one Hong Kong
// writes only in the Traditional form (PhraseCharacters), so Hong Kong usage such as 了解, 公布, 台下, 上周 or 加注 stays;
// no other entry starts inside it and runs past its end, which would mean the segmentation is in doubt; and s2hk keeps
// its length. A Traditional word OpenCC does not list (說明 in 說明了, 如何 in 演示了如何) cannot protect a neighbour this
// way, which is why the character list is short: see docs/phases/p4-results.zh-HK.md for the Week 3 evidence.
public sealed class OpenccNoteScript : INoteScript
{
    // OpenCC lists these as rare Traditional characters too, but modern Traditional writing uses them only as
    // Simplified forms. The other shared characters it would change stay (游泳, 划船, 茶几, 葵涌, 伙伴, 蚝油, 着).
    const string AlsoSimplified = "价确党愿叶蜡荐苹淀胜种帘万适虫腊广厂么极坏";

    // Shared characters whose Traditional form is the only one Hong Kong writes in the words OpenCC converts them in
    // (示范 → 示範, 根据 → 根據, 由于 → 由於, 背后 → 背後, 干净 → 乾淨, 丰富 → 豐富). Left out on purpose: characters Hong Kong
    // also writes in the Simplified-looking form (了解, 公布, 宣布, 上周, 周末, 加注, 游泳, 着, 伙伴, 升, 岩, 佣金) and
    // characters too common in correct Traditional to risk a wrong segmentation (表, 回, 出, 系, 卷, 致, 板). 面 is listed
    // because OpenCC also lists the common words that end in it (方面, 前面, 表面), which then claim it first.
    public const string PhraseCharacters = "后于里干几余面制范准据征松丰只涂采筑柜尸舍仆郁胡姜咸杰托划斗丑谷云占杆杠夸御辟雇愈";

    static readonly Lazy<HashSet<char>> SimplifiedOnly = new(Load);
    // OpenCC's STPhrases keys: the lexicon its own segmentation uses.
    static readonly Lazy<HashSet<string>> Phrases = new(() => DictionaryLib.Provider.st_phrases.Dict.Keys.Where(x => x.Length > 1).ToHashSet());
    const int LongestPhrase = 16;
    readonly Opencc converter = new("s2hk");

    public string Apply(string text, string language)
    {
        if (language != "zh-Hant") return text;
        var only = SimplifiedOnly.Value;
        var output = text.ToCharArray();
        if (text.Any(only.Contains))
        {
            var converted = converter.Convert(text);
            // s2hk keeps the length of these texts; if it ever does not, each Simplified-only character converts on its own.
            if (converted.Length != text.Length)
            {
                for (var i = 0; i < output.Length; i++)
                    if (only.Contains(output[i]) && converter.Convert(output[i].ToString()) is { Length: 1 } single) output[i] = single[0];
            }
            else
                for (var i = 0; i < output.Length; i++)
                    if (only.Contains(text[i]) || i > 0 && only.Contains(text[i - 1]) || i + 1 < text.Length && only.Contains(text[i + 1]))
                        output[i] = converted[i];
        }
        if (text.Any(x => PhraseCharacters.Contains(x)))
            foreach (var (start, traditional) in SharedPhrases(text, only))
                for (var k = 0; k < traditional.Length; k++)
                    if (output[start + k] == text[start + k]) output[start + k] = traditional[k];
        return new string(output);
    }

    // Longest-match segmentation over OpenCC's phrase list, as OpenCC itself segments; yields the shared-character
    // phrases that convert under the rules above, with their s2hk form.
    IEnumerable<(int Start, string Traditional)> SharedPhrases(string text, HashSet<char> only)
    {
        var phrases = Phrases.Value.GetAlternateLookup<ReadOnlySpan<char>>();
        var i = 0;
        while (i < text.Length)
        {
            var length = Match(phrases, text, i);
            if (length == 0) { i++; continue; }
            var end = i + length;
            var phrase = text.Substring(i, length);
            if (!phrase.Any(only.Contains) && converter.Convert(phrase) is { } traditional && traditional.Length == length && traditional != phrase &&
                Enumerable.Range(0, length).All(k => traditional[k] == phrase[k] || PhraseCharacters.Contains(phrase[k])) &&
                !Enumerable.Range(i + 1, length - 1).Any(p => Match(phrases, text, p) > end - p))
                yield return (i, traditional);
            i = end;
        }
    }

    static int Match(HashSet<string>.AlternateLookup<ReadOnlySpan<char>> phrases, string text, int start)
    {
        for (var length = Math.Min(LongestPhrase, text.Length - start); length >= 2; length--)
            if (phrases.Contains(text.AsSpan(start, length))) return length;
        return 0;
    }

    static HashSet<char> Load()
    {
        var simplified = Read("OpenCC.STCharacters.txt");
        var traditional = Read("OpenCC.TSCharacters.txt");
        return simplified.Where(x => x.Key.Length == 1 && !x.Value.Contains(x.Key) && !traditional.ContainsKey(x.Key))
            .Select(x => x.Key[0]).Concat(AlsoSimplified).ToHashSet();
    }

    static Dictionary<string, string[]> Read(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("OpenCC dictionary " + resource + " is not embedded");
        using var reader = new StreamReader(stream);
        var entries = new Dictionary<string, string[]>();
        while (reader.ReadLine() is { } line)
            if (line.Split('\t') is [var key, var candidates]) entries[key] = candidates.Split(' ');
        return entries;
    }
}
