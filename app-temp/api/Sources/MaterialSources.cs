using Playback.Domain.Models;
using PassageReader = Playback.Application.Notes.MaterialSources;

namespace Playback.Api.Sources;

public static class MaterialSources
{
    public static IEnumerable<Material> Passages(IEnumerable<Material> materials) => PassageReader.Passages(materials.Select(NoteStoreAdapter.Read))
        .Select(x => new Material { Id = x.Id, SessionId = x.SessionId, Name = x.Name, Text = x.Text });
    public static string Parent(string id) => PassageReader.Parent(id);
    public static IEnumerable<string> Allowed(SessionView session) => PassageReader.Allowed(NoteStoreAdapter.Read(session));
}
