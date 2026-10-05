using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MongoDB.Driver;
using Microsoft.Extensions.Primitives;
using MongoDB.Bson.Serialization.Attributes;

namespace Playback.Api.Db;

// Saved audio chunks: the WAV on disk, its identity, and its ASR status.
public sealed class ChunkRecord
{
    [BsonId] public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string SourceId { get; set; } = "";
    public long Sequence { get; set; }
    public long StartMs { get; set; }
    public long EndMs { get; set; }
    public DateTime? RecordedAt { get; set; }
    public string Hash { get; set; } = "";
    public string Status { get; set; } = ChunkStatus.PendingAsr;
    public string? Error { get; set; }
    public int AsrAttempts { get; set; }
    [BsonIgnore] public string Path { get; set; } = "";
}

// Where a chunk is in transcription. The web shows these values as they are.
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

public partial class PlaybackStore
{
    public async Task<ChunkRecord> SaveLocalChunk(
        string sessionId, string sourceId, long sequence, long startMs, long endMs,
        string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        stream.Position = 0;
        var form = new FormCollection(new Dictionary<string, StringValues>
        {
            ["sessionId"] = sessionId,
            ["sourceId"] = sourceId,
            ["sequence"] = sequence.ToString(CultureInfo.InvariantCulture),
            ["startMs"] = startMs.ToString(CultureInfo.InvariantCulture),
            ["endMs"] = endMs.ToString(CultureInfo.InvariantCulture),
            ["sha256"] = hash,
            ["recordedAt"] = File.GetLastWriteTimeUtc(path).AddMilliseconds(-(endMs - startMs)).ToString("O", CultureInfo.InvariantCulture)
        });
        var file = new FormFile(stream, 0, stream.Length, "file", "capture.wav");
        return await SaveChunk(form, file, ct);
    }
    public async Task<ChunkRecord> SaveChunk(IFormCollection form, IFormFile file, CancellationToken ct)
    {
        string Required(string key) => form[key].ToString() is { Length: > 0 } value ? value : throw new InvalidOperationException($"Missing {key}");
        var session = Required("sessionId");
        var source = Required("sourceId");
        if (!ChunkIdentity.IsSessionId(session) || !ChunkIdentity.IsSourceId(source))
            throw new InvalidOperationException("Invalid session or source");
        if (!long.TryParse(Required("sequence"), out var sequence) || sequence < 0 ||
            !long.TryParse(Required("startMs"), out var start) ||
            !long.TryParse(Required("endMs"), out var end) || end <= start)
            throw new InvalidOperationException("Invalid time range or sequence");
        var hash = Required("sha256").ToLowerInvariant();
        if (!ChunkIdentity.IsSha256(hash)) throw new InvalidOperationException("Invalid SHA-256");
        if (await Collection<SessionRecord>("sessions").CountDocumentsAsync(x => x.Id == session) == 0)
            throw new InvalidOperationException("Session not found");
        var existing = await Collection<ChunkRecord>("chunks")
            .Find(x => x.SessionId == session && x.SourceId == source && x.Sequence == sequence)
            .FirstOrDefaultAsync();
        if (existing is not null && existing.Hash != hash) throw new InvalidOperationException("Sequence already has different audio");
        var id = ChunkIdentity.Id(session, source, sequence, hash);
        var path = ChunkIdentity.WavPath(audioRoot, session, source, sequence, hash);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var output = File.Create(temporary)) await file.CopyToAsync(output, ct);
            await using (var stream = File.OpenRead(temporary))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
                if (actual != hash) throw new InvalidOperationException("Chunk hash mismatch");
                stream.Position = 0;
                var header = new byte[12]; await stream.ReadExactlyAsync(header, ct);
                if (Encoding.ASCII.GetString(header, 0, 4) != "RIFF" ||
                    Encoding.ASCII.GetString(header, 8, 4) != "WAVE")
                    throw new InvalidOperationException("Expected RIFF/WAVE audio");
            }
            File.Move(temporary, path, false);
        }
        catch (IOException) when (File.Exists(path)) { File.Delete(temporary); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        var recordedAt = DateTime.TryParse(form["recordedAt"].ToString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed.ToUniversalTime() : DateTime.UtcNow.AddMilliseconds(-(end - start));
        var chunk = existing ?? new ChunkRecord
        {
            Id = id,
            SessionId = session,
            SourceId = source,
            Sequence = sequence,
            StartMs = start,
            EndMs = end,
            RecordedAt = recordedAt,
            Hash = hash
        };
        chunk.Path = path;
        await Collection<ChunkRecord>("chunks").ReplaceOneAsync(x => x.Id == id, chunk, new ReplaceOptions { IsUpsert = true }, ct);
        Touch(session, "chunk", [id]);
        return chunk;
    }
    public async Task<ChunkRecord?> Chunk(string id)
    {
        var chunk = await Collection<ChunkRecord>("chunks").Find(x => x.Id == id).FirstOrDefaultAsync();
        if (chunk is not null) chunk.Path = ChunkIdentity.WavPath(audioRoot, chunk.SessionId, chunk.SourceId, chunk.Sequence, chunk.Hash);
        return chunk;
    }
    public string? ChunkAudioPath(string chunkId) =>
        ChunkIdentity.WavPath(audioRoot, chunkId) is { } path && File.Exists(path) ? path : null;
    public async Task<(long Awaiting, long Failed)> MigrateLegacyAsrStatuses()
    {
        var waiting = await Collection<ChunkRecord>("chunks").UpdateManyAsync(
            x => x.Status == ChunkStatus.LegacyAwaitingConsent,
            Builders<ChunkRecord>.Update.Set(x => x.Status, ChunkStatus.PendingAsr));
        var filter = Builders<ChunkRecord>.Filter.Eq(x => x.Status, ChunkStatus.AsrError) &
            Builders<ChunkRecord>.Filter.Exists(x => x.AsrAttempts, false);
        var result = await Collection<ChunkRecord>("chunks").UpdateManyAsync(filter,
            Builders<ChunkRecord>.Update.Set(x => x.Status, ChunkStatus.AsrManual));
        return (waiting.ModifiedCount, result.ModifiedCount);
    }
    public async Task<List<ChunkRecord>> PendingAsrChunks() => await Collection<ChunkRecord>("chunks")
        .Find(x => x.Status == ChunkStatus.PendingAsr || x.Status == ChunkStatus.Transcribing || x.Status == ChunkStatus.AsrError)
        .ToListAsync();
    public async Task<List<ChunkRecord>> AudioWindowChunks(string sessionId, long startMs, long endMs, string? sourceId)
    {
        var filter = Builders<ChunkRecord>.Filter.Eq(x => x.SessionId, sessionId) &
            Builders<ChunkRecord>.Filter.Lt(x => x.StartMs, endMs) &
            Builders<ChunkRecord>.Filter.Gt(x => x.EndMs, startMs);
        if (sourceId is not null) filter &= Builders<ChunkRecord>.Filter.Eq(x => x.SourceId, sourceId);
        return await Collection<ChunkRecord>("chunks").Find(filter).SortBy(x => x.StartMs).ToListAsync();
    }
    public async Task<bool> RecordAsrFailure(string id, string error, bool stopRetries = false)
    {
        var chunk = await Chunk(id) ?? throw new InvalidOperationException("Chunk not found");
        var attempts = chunk.AsrAttempts + 1;
        await Collection<ChunkRecord>("chunks").UpdateOneAsync(x => x.Id == id,
            Builders<ChunkRecord>.Update
                .Set(x => x.Status, stopRetries || attempts >= ChunkStatus.MaxAutomaticAsrAttempts ? ChunkStatus.AsrManual : ChunkStatus.AsrError)
                .Set(x => x.Error, error)
                .Set(x => x.AsrAttempts, attempts));
        Touch(chunk.SessionId, "chunk", [id]);
        return !stopRetries && attempts < ChunkStatus.MaxAutomaticAsrAttempts;
    }
    public async Task<ChunkRecord> RetryAsr(string sessionId, string id)
    {
        var chunk = await Collection<ChunkRecord>("chunks")
            .Find(x => x.Id == id && x.SessionId == sessionId).FirstOrDefaultAsync()
            ?? throw new InvalidOperationException("Audio chunk is not in this session");
        if (chunk.Status is not (ChunkStatus.AsrError or ChunkStatus.AsrManual or ChunkStatus.AsrEmpty))
            throw new InvalidOperationException("Only failed or empty ASR chunks can be retried");
        if (chunk.Status == ChunkStatus.AsrEmpty)
        {
            await Collection<Transcript>("transcripts").DeleteOneAsync(x => x.Id == id && x.SessionId == sessionId);
            Touch(sessionId, "transcript", [id]);
        }
        await Collection<ChunkRecord>("chunks").UpdateOneAsync(x => x.Id == id,
            Builders<ChunkRecord>.Update.Set(x => x.Status, ChunkStatus.PendingAsr)
                .Set(x => x.Error, null)
                .Set(x => x.AsrAttempts, chunk.Status == ChunkStatus.AsrManual ? 0 : chunk.AsrAttempts));
        Touch(sessionId, "chunk", [id]);
        return chunk;
    }
    public async Task SetChunkStatus(string id, string status, string? error = null) {
        await Collection<ChunkRecord>("chunks").UpdateOneAsync(x => x.Id == id,
            Builders<ChunkRecord>.Update.Set(x => x.Status, status).Set(x => x.Error, error));
        var chunk = await Chunk(id); if (chunk is not null) Touch(chunk.SessionId, "chunk", [id]);
    }
}
