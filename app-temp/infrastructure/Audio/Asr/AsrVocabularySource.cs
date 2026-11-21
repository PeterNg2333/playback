using System.Collections.Concurrent;
using Playback.Domain.Audio;
using Playback.Infrastructure.Storage;

namespace Playback.Infrastructure.Audio.Asr;

// Each session's expected vocabulary for speech recognition, from its materials. Every chunk asks, so a session's list
// is kept for two minutes; a material added meanwhile is picked up after that.
public sealed class AsrVocabularySource(PlaybackStore store)
{
    static readonly TimeSpan Keep = TimeSpan.FromMinutes(2);
    readonly ConcurrentDictionary<string, (DateTime At, IReadOnlyList<string> Terms)> cache = new();

    public async Task<IReadOnlyList<string>> Terms(string sessionId, CancellationToken ct)
    {
        if (cache.TryGetValue(sessionId, out var known) && DateTime.UtcNow - known.At < Keep) return known.Terms;
        var texts = await store.MaterialTexts(sessionId);
        IReadOnlyList<string> terms = texts.Count == 0 ? [] : AsrVocabulary.From(texts);
        cache[sessionId] = (DateTime.UtcNow, terms);
        return terms;
    }
}
