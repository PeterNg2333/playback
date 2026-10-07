using Playback.Domain.Notes;
using Playback.Api.Terms;
using Playback.Api.Sources;
using System.Text.RegularExpressions;

namespace Playback.Api.Db;

public sealed class DemoUser
{
    public string Username { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
