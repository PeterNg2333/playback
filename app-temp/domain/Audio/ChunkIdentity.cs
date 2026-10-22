using System.Text.RegularExpressions;
namespace Playback.Domain.Audio;

public static class ChunkStatus
{
    public const string PendingAsr = "pending-asr";
    public const string Transcribing = "transcribing";
    // Failed; the queue retries it until MaxAutomaticAsrAttempts.
    public const string AsrError = "asr-error";
    // Automatic retries stopped; the user can retry it from the transcript.
    public const string AsrManual = "asr-manual";
    public const string Transcribed = "transcribed";
    public const string AsrEmpty = "asr-empty";
    // Exact digital silence; never uploaded.
    public const string Silent = "silent";
    // The local VAD found no speech; never uploaded.
    public const string VadSilence = "vad-silence";
    // Saved by builds that asked for consent before upload; now queued like any new chunk.
    public const string LegacyAwaitingConsent = "awaiting-consent";

    public const int MaxAutomaticAsrAttempts = 3;

    // The ASR queue still owes this chunk a transcript.
    public static bool AwaitingTranscript(string status) => status is PendingAsr or Transcribing or AsrError;

    // ASR is done with this chunk: text, no words, or nothing worth sending.
    public static bool Finished(string status) => status is Transcribed or AsrEmpty or Silent or VadSilence;
}

// A chunk is identified by session, source, sequence and audio hash, so a retried upload of the
// same audio finds the same record and the same WAV file.
public static class ChunkIdentity
{
    static readonly Regex SessionId = new("^[a-f0-9]{32}$");
    static readonly Regex SourceId = new("^[a-zA-Z0-9_]{1,40}$");
    static readonly Regex Sha256 = new("^[a-f0-9]{64}$");

    public static bool IsSessionId(string value) => SessionId.IsMatch(value);
    // Capture streams use the same 32-hex form as sessions.
    public static bool IsStreamId(string value) => SessionId.IsMatch(value);
    public static bool IsSourceId(string value) => SourceId.IsMatch(value);
    public static bool IsSha256(string value) => Sha256.IsMatch(value);

    public static string Id(string sessionId, string sourceId, long sequence, string hash) =>
        $"{sessionId}-{sourceId}-{sequence}-{hash}";

    public static string WavPath(string audioFolder, string sessionId, string sourceId, long sequence, string hash) =>
        Path.Combine(audioFolder, sessionId, sourceId, $"{sequence}-{hash}.wav");

    // The WAV path named by a chunk ID from a URL, or null when the ID is not a chunk identity.
    public static string? WavPath(string audioFolder, string chunkId)
    {
        var parts = chunkId.Split('-');
        if (parts.Length != 4 || !IsSessionId(parts[0]) || !IsSourceId(parts[1]) ||
            !long.TryParse(parts[2], out _) || !IsSha256(parts[3])) return null;
        return Path.Combine(audioFolder, parts[0], parts[1], $"{parts[2]}-{parts[3]}.wav");
    }
}
