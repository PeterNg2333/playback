using Playback.Domain.Notes;

namespace Playback.Application.Notes;

// When an AI text edit applies on its own. Added content, moves and new topics always do. A replace-text applies
// when it is small and keeps every number, formula, code span and condition it touches (citations are already
// enforced by the tree). A deletion applies when at most two blocks go in one reply and every source they cite
// stays cited elsewhere. Anything else waits as a suggestion for a person, so people review only real risks.
public static class NoteEditPolicy
{
    public const int MaxAutomaticDeletes = 2;
    public const int MaxAutomaticEditCharacters = 300;

    // Null when the edit may apply on its own; otherwise why a person should look at it.
    public static string? TextEditReview(string find, string replace)
    {
        if (find.Length > MaxAutomaticEditCharacters || replace.Length > MaxAutomaticEditCharacters)
            return $"the edit changes more than {MaxAutomaticEditCharacters} characters";
        var lost = NoteRewrite.ProtectedFragments(find).FirstOrDefault(x => !replace.Contains(x, StringComparison.Ordinal));
        return lost is null ? null : $"the edit would drop \"{(lost.Length > 40 ? lost[..40] + "…" : lost)}\"";
    }
}
