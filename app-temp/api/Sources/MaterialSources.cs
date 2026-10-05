using Playback.Api.Db;
using System.Text.RegularExpressions;

namespace Playback.Api.Sources;

// Immutable material text is read in durable bounded passages, with exact offsets in the original.
// A covered passage never means the whole uploaded material was digested.
public static class MaterialSources
{
    public static IEnumerable<Material> Passages(IEnumerable<Material> materials) {
        foreach (var material in materials) {
            if (material.Text.Length <= 3000) { yield return material; continue; }
            for (var start = 0; start < material.Text.Length;) {
                var end = Math.Min(start + 3000, material.Text.Length);
                var boundary = material.Text.LastIndexOf('\n', end - 1, end - start);
                if (end < material.Text.Length && boundary > start + 1500) end = boundary + 1;
                var text = material.Text[start..end];
                yield return new Material { Id = $"{material.Id}_p{start}_{end}_{ContentHash.Of(text)[..12]}",
                    SessionId = material.SessionId, Name = $"{material.Name} (characters {start + 1}–{end})", Text = text };
                start = end;
            }
        }
    }
    public static string Parent(string id) => Regex.Match(id, @"^([a-f0-9]{32})_p\d+_\d+_[a-f0-9]{12}$") is { Success: true } match ? match.Groups[1].Value : id;
    public static IEnumerable<string> Allowed(SessionView session) => session.Transcripts.Select(x => x.Id)
        .Concat(session.Materials.Select(x => x.Id)).Concat(Passages(session.Materials).Select(x => x.Id));
}
