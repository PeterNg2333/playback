using System.Security.Cryptography;
using System.Text;
using Playback.Application.Audio;
using Playback.Domain.Audio;

namespace Playback.Infrastructure.Audio;

// RIFF chunks are parsed and PCM frames streamed; original WAV headers are never concatenated.
public sealed class WavCombinedAudioFiles(string root, Func<string, Task<string?>> originalPath) : ICombinedAudioFiles
{
    sealed record Layout(byte[] Descriptor, AudioFormat Format, long DataOffset, long DataLength);
    static Layout Read(string path)
    {
        using var stream = File.OpenRead(path); using var reader = new BinaryReader(stream, Encoding.ASCII);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new InvalidOperationException("Expected RIFF audio");
        var length = reader.ReadUInt32();
        if (length + 8L != stream.Length || Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new InvalidOperationException("Incomplete WAV");
        byte[]? format = null; long dataOffset = -1, dataLength = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            var kind = Encoding.ASCII.GetString(reader.ReadBytes(4)); var size = reader.ReadUInt32(); var end = stream.Position + size;
            if (end > stream.Length) throw new InvalidOperationException("Invalid WAV chunk size");
            if (kind == "fmt ") { if (size is < 16 or > 256) throw new InvalidOperationException("Unsupported WAV format"); format = reader.ReadBytes((int)size); }
            if (kind == "data") { if (dataOffset >= 0) throw new InvalidOperationException("Multiple WAV data chunks"); dataOffset = stream.Position; dataLength = size; }
            stream.Position = end + (size % 2);
        }
        if (format is null || dataOffset < 0) throw new InvalidOperationException("WAV requires format and data");
        var encoding = BitConverter.ToUInt16(format, 0); var channels = BitConverter.ToUInt16(format, 2);
        var sampleRate = BitConverter.ToInt32(format, 4); var align = BitConverter.ToUInt16(format, 12); var bits = BitConverter.ToUInt16(format, 14);
        if (encoding is not (1 or 3 or 65534) || channels is < 1 or > 8 || sampleRate is < 8000 or > 192000 ||
            bits is not (16 or 24 or 32) || align != channels * bits / 8 || dataLength % align != 0)
            throw new InvalidOperationException("Unsupported or unaligned PCM format");
        if (encoding == 65534 && (format.Length < 40 || BitConverter.ToInt32(format, 24) is not (1 or 3))) throw new InvalidOperationException("Unsupported extensible WAV");
        if ((encoding == 3 || encoding == 65534 && BitConverter.ToInt32(format, 24) == 3) && bits != 32) throw new InvalidOperationException("Unsupported floating-point WAV");
        return new(format, new(encoding, sampleRate, channels, bits, align, Convert.ToBase64String(format)), dataOffset, dataLength);
    }
    public async Task<(AudioFormat Format, long Frames)> Inspect(string id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var layout = Read(await originalPath(id) ?? throw new InvalidOperationException("Original audio unavailable"));
        return (layout.Format, layout.DataLength / layout.Format.BlockAlign);
    }
    public string PathFor(string sessionId, string id)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(sessionId, "^[a-f0-9]{32}$") || !System.Text.RegularExpressions.Regex.IsMatch(id, "^(?:quiet|rerun)_[a-f0-9]{64}$")) throw new InvalidOperationException("Invalid derived audio identity");
        return Path.Combine(root, sessionId, "combined", id + ".wav");
    }
    public async Task<bool> Valid(CombinedAudioSegment segment, CancellationToken ct)
    {
        var path = PathFor(segment.SessionId, segment.Id);
        if (!File.Exists(path)) return false;
        try
        {
            var layout = Read(path);
            if (layout.Format != segment.Format || layout.DataLength / layout.Format.BlockAlign != segment.Children.Sum(x => x.FrameCount)) return false;
            await using var input = File.OpenRead(path);
            return Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).ToLowerInvariant() == segment.DerivedHash;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or EndOfStreamException) { return false; }
    }
    public async Task<(string Hash, string StorageKey)> Publish(CombinedAudioSegment segment, CancellationToken ct)
    {
        var path = PathFor(segment.SessionId, segment.Id); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".part-" + Guid.NewGuid().ToString("N");
        var descriptor = Convert.FromBase64String(segment.Format.Descriptor);
        var dataLength = checked(segment.Children.Sum(x => x.FrameCount) * segment.Format.BlockAlign);
        var total = 12L + 8 + descriptor.Length + descriptor.Length % 2 + 8 + dataLength + dataLength % 2;
        if (dataLength <= 0 || total > uint.MaxValue) throw new InvalidOperationException("Derived audio exceeds RIFF bounds");
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write((uint)(total - 8)); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write((uint)descriptor.Length); writer.Write(descriptor); if (descriptor.Length % 2 != 0) writer.Write((byte)0);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write((uint)dataLength); writer.Flush();
                var buffer = new byte[65536]; long offset = 0;
                foreach (var child in segment.Children)
                {
                    var original = await originalPath(child.ChunkId) ?? throw new InvalidOperationException("Original audio unavailable");
                    var layout = Read(original);
                    if (layout.Format != segment.Format || layout.DataLength != child.FrameCount * segment.Format.BlockAlign || child.FrameOffset != offset) throw new InvalidOperationException("Manifest differs from original samples");
                    await using var input = File.OpenRead(original);
                    if (Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).ToLowerInvariant() != child.OriginalHash) throw new InvalidOperationException("Original audio hash changed");
                    input.Position = layout.DataOffset; long remaining = layout.DataLength;
                    while (remaining > 0) { var count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), ct); if (count == 0) throw new EndOfStreamException(); await output.WriteAsync(buffer.AsMemory(0, count), ct); remaining -= count; }
                    offset += child.FrameCount;
                }
                if (dataLength % 2 != 0) writer.Write((byte)0);
                await output.FlushAsync(ct); output.Flush(flushToDisk: true);
            }
            var verified = Read(temporary);
            if (verified.DataLength != dataLength) throw new InvalidOperationException("Derived WAV validation failed");
            await using var completed = File.OpenRead(temporary);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(completed, ct)).ToLowerInvariant();
            await completed.DisposeAsync();
            File.Move(temporary, path, overwrite: true); // Deterministic path; a pre-publication crash is recoverable.
            return (hash, $"{segment.SessionId}/combined/{segment.Id}.wav");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
