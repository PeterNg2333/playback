namespace Playback.Domain.Notes;

// What a session's notes are written as: lecture study notes (the default) or meeting minutes.
public static class NoteKinds
{
    public const string Lecture = "lecture";
    public const string Meeting = "meeting";

    public static void Validate(string kind)
    {
        if (kind is not (Lecture or Meeting))
            throw new InvalidOperationException("Note type must be lecture or meeting");
    }

    // Sessions saved before note types existed have none: they are lecture notes.
    public static string Normalize(string? kind) => kind == Meeting ? Meeting : Lecture;
}
