using Playback.Api.Db;
using System.Text.RegularExpressions;
using Playback.Api.Sources;

namespace Playback.Api.Notes;

public static class NoteChangeLog
{
    public static List<NoteEdit> Build(string before, string after,
        IEnumerable<string> transcriptIds, IEnumerable<string> materialIds)
    {
        if (before == after) return [];
        string[] oldLines = before.Length == 0 ? [] : before.Split('\n');
        string[] newLines = after.Length == 0 ? [] : after.Split('\n');
        var transcriptSet = transcriptIds.ToHashSet(StringComparer.Ordinal);
        var materialSet = materialIds.ToHashSet(StringComparer.Ordinal);
        var edits = new List<NoteEdit>();
        NoteEdit Edit(string kind, int line, string text)
        {
            var citations = SourceReferences.CitationBodies(text)
                .Where(x => !x.StartsWith("ref:", StringComparison.OrdinalIgnoreCase))
                .SelectMany(x => Regex.Matches(x, @"[A-Za-z0-9_-]+").Select(token => token.Value))
                .Distinct().ToList();
            return new NoteEdit
            {
                Kind = kind,
                Line = line,
                Text = text,
                TranscriptIds = citations.Where(transcriptSet.Contains).ToList(),
                MaterialIds = citations.Where(materialSet.Contains).ToList()
            };
        }

        var prefix = 0;
        while (prefix < oldLines.Length && prefix < newLines.Length && oldLines[prefix] == newLines[prefix]) prefix++;
        var oldEnd = oldLines.Length;
        var newEnd = newLines.Length;
        while (oldEnd > prefix && newEnd > prefix && oldLines[oldEnd - 1] == newLines[newEnd - 1])
        {
            oldEnd--;
            newEnd--;
        }
        var oldCount = oldEnd - prefix;
        var newCount = newEnd - prefix;
        if ((long)oldCount * newCount > 1_500_000 || oldCount + newCount > 3_000)
        {
            if (oldCount > 0) edits.Add(Edit("remove", prefix + 1, string.Join('\n', oldLines.Skip(prefix).Take(oldCount))));
            if (newCount > 0) edits.Add(Edit("insert", prefix + 1, string.Join('\n', newLines.Skip(prefix).Take(newCount))));
            return edits;
        }

        var lengths = new int[oldCount + 1, newCount + 1];
        for (var old = oldCount - 1; old >= 0; old--)
            for (var next = newCount - 1; next >= 0; next--)
                lengths[old, next] = oldLines[prefix + old] == newLines[prefix + next]
                    ? lengths[old + 1, next + 1] + 1
                    : Math.Max(lengths[old + 1, next], lengths[old, next + 1]);
        var oldIndex = 0;
        var newIndex = 0;
        while (oldIndex < oldCount || newIndex < newCount)
        {
            if (oldIndex < oldCount && newIndex < newCount &&
                oldLines[prefix + oldIndex] == newLines[prefix + newIndex])
            {
                oldIndex++;
                newIndex++;
            }
            else if (oldIndex < oldCount &&
                     (newIndex == newCount || lengths[oldIndex + 1, newIndex] >= lengths[oldIndex, newIndex + 1]))
            {
                edits.Add(Edit("remove", prefix + oldIndex + 1, oldLines[prefix + oldIndex]));
                oldIndex++;
            }
            else
            {
                edits.Add(Edit("insert", prefix + newIndex + 1, newLines[prefix + newIndex]));
                newIndex++;
            }
        }
        return edits;
    }
}
