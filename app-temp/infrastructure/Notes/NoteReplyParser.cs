using System.Text.Json;
using System.Text.RegularExpressions;
using Playback.Domain.Notes;

namespace Playback.Infrastructure.Notes;

// Provider JSON is an untrusted proposal. Domain mutation validates it before any persistence.
public static class NoteReplyParser
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    public static NotePatch Parse(string text)
    {
        text = Regex.Replace(text.Trim(), @"^```(?:json)?\s*|\s*```$", "");
        return JsonSerializer.Deserialize<NotePatch>(text, Options) ?? throw new InvalidOperationException("Empty section response");
    }
}
