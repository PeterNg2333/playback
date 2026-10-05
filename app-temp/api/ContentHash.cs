using System.Security.Cryptography;
using System.Text;

namespace Playback.Api;

// Lowercase SHA-256 hex of a text: the identity of a prompt, an input, a passage or a saved record.
public static class ContentHash
{
    public static string Of(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
