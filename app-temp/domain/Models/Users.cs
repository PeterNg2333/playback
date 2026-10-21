using Playback.Domain.Notes;
using System.Text.RegularExpressions;

namespace Playback.Domain.Models;

public sealed class DemoUser
{
    public string Username { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
