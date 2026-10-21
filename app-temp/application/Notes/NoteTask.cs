namespace Playback.Application.Notes;

// One kind of note model call: its name (shown in activity), the JSON schema its reply must match and its output cap.
// The schema is plain JSON Schema with every object closed and every property required, which both OpenRouter's strict
// structured outputs and Gemini accept.
public sealed record NoteTask(string Name, string Schema, int MaxOutputTokens);
