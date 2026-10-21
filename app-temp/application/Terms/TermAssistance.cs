namespace Playback.Application.Assistance;
public sealed class TermAssistance(ITermClassifier classifier, ITextGenerator writer)
{
    public Task<JevRankResult> Rank(string term, string context, CancellationToken ct) => classifier.Rank(term, ct, context);
    public Task<GroundedResult> Explain(string term, CancellationToken ct)
    {
        if (term.Length is < 1 or > 4000) throw new InvalidOperationException("Invalid explanation request");
        return writer.GroundedExplain(term, ct);
    }
}
