using Playback.Application.Notes;
using Playback.Domain.Notes;
using System.Text.Json;
using System.Diagnostics;

namespace Playback.Infrastructure.Notes;

public class JevNoteGate(IJevTransport transport) : INoteGate
{
    public const string PromptVersion = NoteGatePrompt.PromptVersion;
    public static object Questions => NoteGatePrompt.Questions;
    public virtual bool IsConfigured => transport.IsConfigured;
    public virtual async Task<NoteGateDecision> Decide(string state, CancellationToken ct) {
        var watch = Stopwatch.StartNew();
        var response = await transport.Evaluate(state, Questions, ct);
        var probability = response.Answers.GetProperty("update").GetProperty("noul").GetDouble();
        var choice = response.Answers.GetProperty("action").GetProperty("choice").GetString();
        var confidence = response.Answers.GetProperty("action").GetProperty("confidence").GetDouble();
        return new NoteGateDecision(NoteAdmissionPolicy.AllowsDecision(choice, probability, confidence),
            probability, confidence, response.Model, response.Usage, watch.ElapsedMilliseconds, choice);
    }
}

// Existing bounded HTTP transport stays in the API until the remaining provider migration.
public interface IJevTransport
{
    bool IsConfigured { get; }
    Task<(string Model, JsonElement Answers, string? Usage)> Evaluate(string state, object questions, CancellationToken ct);
}
