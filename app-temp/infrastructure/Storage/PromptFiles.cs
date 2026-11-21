using System.Collections.Concurrent;
using Playback.Domain;
using Playback.Domain.Audio;

namespace Playback.Infrastructure.Storage;

// Model instructions are the same text across many calls, so each is kept once as <data>/prompts/<sha256>.txt instead of in
// every AI activity document. A file is written once and never changed, which a Cloud Storage mount handles. A hash always
// names the same text, so texts stay in memory once saved or read: repeated saves and activity polls touch the mount once.
public sealed class PromptFiles(string folder)
{
    // A few dozen instruction texts of a few KB exist; the cap only bounds memory if that ever changes.
    const int CacheLimit = 1024;
    readonly ConcurrentDictionary<string, string> cache = new();

    // False when the text could not be kept, so the caller stores it in the document instead.
    public async Task<bool> Save(string hash, string text)
    {
        if (!ChunkIdentity.IsSha256(hash) || ContentHash.Of(text) != hash) return false;
        if (cache.ContainsKey(hash)) return true;
        var path = Path.Combine(folder, hash + ".txt");
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(folder);
                await File.WriteAllTextAsync(temporary, text);
                File.Move(temporary, path, false);
            }
        }
        catch (IOException) when (File.Exists(path)) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        Remember(hash, text);
        return true;
    }

    public async Task<string?> Read(string hash)
    {
        if (!ChunkIdentity.IsSha256(hash)) return null;
        if (cache.TryGetValue(hash, out var known)) return known;
        try
        {
            var text = await File.ReadAllTextAsync(Path.Combine(folder, hash + ".txt"));
            if (ContentHash.Of(text) != hash) return null;
            Remember(hash, text);
            return text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    void Remember(string hash, string text)
    {
        if (cache.Count < CacheLimit) cache.TryAdd(hash, text);
    }
}
