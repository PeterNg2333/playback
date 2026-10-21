namespace Playback.Domain.Audio;

// Fixed recording rules: a speech boundary is this much silence after speech, and quiet chunks are combined into files of
// at most this length for playback.
public static class AudioPolicy
{
    public const double SilenceBoundarySeconds = 2;
    public const bool CombineQuiet = true;
    public const double MaxCombinedSeconds = 60;
}
