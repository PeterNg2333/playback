namespace Playback.Api;

// Compatibility for unmigrated features; hashing has one implementation.
public static class ContentHash
{
    public static string Of(string text) => Playback.Domain.ContentHash.Of(text);
}
