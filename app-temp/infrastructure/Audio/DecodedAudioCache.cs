namespace Playback.Infrastructure.Audio;

// Opus chunks are decoded to WAV when read, into a bounded folder on local disk (memory on Cloud Run). The most recently
// used files stay; older ones go once the folder passes its limit, so readers keep working on plain WAV paths.
public sealed class DecodedAudioCache(string folder, long limitBytes = 128L * 1024 * 1024)
{
    public string Path(string chunkId, string opusPath)
    {
        var path = System.IO.Path.Combine(folder, chunkId + ".wav");
        if (File.Exists(path))
        {
            try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch (IOException) { }
            return path;
        }
        Directory.CreateDirectory(folder);
        try { OpusAudio.Decode(opusPath, path); }
        catch (IOException) when (File.Exists(path)) { }
        Trim(path);
        return path;
    }

    // A deleted session's decoded audio goes with it.
    public void Remove(string sessionId)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, sessionId + "-*.wav"))
            try { File.Delete(file); } catch (IOException) { }
    }

    void Trim(string keep)
    {
        long total = 0;
        foreach (var file in new DirectoryInfo(folder).GetFiles("*.wav").OrderByDescending(x => x.LastWriteTimeUtc))
        {
            total += file.Length;
            if (total > limitBytes && file.FullName != keep)
                try { file.Delete(); } catch (IOException) { }
        }
    }
}
