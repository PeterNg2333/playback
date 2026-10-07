using Playback.Api.Db;
using Inputs = Playback.Application.Notes.NoteInput;

namespace Playback.Api.Notes;

// Read adapter: selected objects remain the caller's instances. All selection rules live in Application.
public static class NoteInput
{
    public static List<Transcript> Pending(SessionView session) => Select(Inputs.Pending(NoteStoreAdapter.Read(session)), session.Transcripts);
    public static List<Transcript> Pending(IEnumerable<Transcript> sources)
    {
        var original = sources.ToList();
        return Select(Inputs.Pending(original.Select(NoteStoreAdapter.Read)), original);
    }
    static List<Transcript> Select(IEnumerable<NoteTranscript> selected, List<Transcript> original) =>
        selected.Select(x => original.First(source => source.Id == x.Id)).ToList();
    public static List<Material> Materials(IEnumerable<Material> sources, Note? latest) =>
        Inputs.Materials(sources.Select(NoteStoreAdapter.Read), latest).Select(x => new Material
            { Id = x.Id, SessionId = x.SessionId, Name = x.Name, Text = x.Text }).ToList();
}
