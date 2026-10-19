using System.Reflection;
using OpenccNetLib;
using Playback.Application.Notes;

namespace Playback.Api.Notes;

// Simplified characters that slip into Traditional notes become Traditional (Hong Kong) with OpenCC's phrase-aware
// s2hk conversion. Only characters that exist solely in Simplified change. A character that is also correct
// Traditional (干, 后, 里, 了, 台, 着…) stays as written, because converting it would turn correct text such as
// 干擾 into 幹擾, unless it stands next to a Simplified-only character: then the phrase is Simplified and OpenCC's
// phrase conversion applies (这里 → 這裡). OpenCC's own lists decide: a character is Simplified-only when it is not
// among its own Traditional candidates in STCharacters and is not itself a Traditional character in TSCharacters.
// A word made only of characters that are also Traditional (示范) is left as written.
public sealed class OpenccNoteScript : INoteScript
{
    // OpenCC lists these as rare Traditional characters too, but modern Traditional writing uses them only as
    // Simplified forms. The other shared characters it would change stay (游泳, 划船, 茶几, 葵涌, 伙伴, 蚝油, 着).
    const string AlsoSimplified = "价确党愿叶蜡荐苹淀胜种帘万适虫腊广厂么极坏";

    static readonly Lazy<HashSet<char>> SimplifiedOnly = new(Load);
    readonly Opencc converter = new("s2hk");

    public string Apply(string text, string language)
    {
        if (language != "zh-Hant" || !text.Any(SimplifiedOnly.Value.Contains)) return text;
        var only = SimplifiedOnly.Value;
        var converted = converter.Convert(text);
        var output = text.ToCharArray();
        // s2hk keeps the length of these texts; if it ever does not, each Simplified-only character converts on its own.
        if (converted.Length != text.Length)
        {
            for (var i = 0; i < output.Length; i++)
                if (only.Contains(output[i]) && converter.Convert(output[i].ToString()) is { Length: 1 } single) output[i] = single[0];
            return new string(output);
        }
        for (var i = 0; i < output.Length; i++)
            if (only.Contains(text[i]) || i > 0 && only.Contains(text[i - 1]) || i + 1 < text.Length && only.Contains(text[i + 1]))
                output[i] = converted[i];
        return new string(output);
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
