namespace Playback.Domain.Notes;
// One atomic head owns the current revision; pending history survives a process crash.
public sealed class NoteHead
{
    public string Id {get;set;} = "";
    public int Version {get;set;}
    public Note? Current {get;set;}
    public bool PendingHistory {get;set;}
}
