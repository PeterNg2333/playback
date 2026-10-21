using Playback.Domain;
using Playback.Domain.Assistance;
using Playback.Domain.Models;
using Playback.Infrastructure.Providers;
using Playback.Infrastructure.Terms;
using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Playback.Infrastructure.Storage;

public partial class PlaybackStore
{
    public async Task<TermInsight?> FindTermInsight(string sessionId, string insightId) =>
        await Collection<TermInsight>("term_insights")
            .Find(x => x.SessionId == sessionId && x.Id == insightId).FirstOrDefaultAsync();
    public async Task<TermInsight> SaveTermRanking(string sessionId, TermCandidate candidate, JevRankResult rank, string language = "zh-Hant")
    {
        var insight = new TermInsight
        {
            Id = TermInsight.IdFor(sessionId, candidate.Text, candidate.Context, language),
            SessionId = sessionId,
            Term = candidate.Text,
            Context = candidate.Context,
            OutputLanguage = language,
            ExplanationVersion = GeminiLanguageModel.ExplanationPromptVersion,
            Highlight = JevTermClassifier.ShouldHighlight(rank),
            JevProbability = rank.JevProbability,
            JevRank = rank.JevRank,
            JevConfidence = rank.JevConfidence,
            JevModel = rank.Model,
            JevCached = rank.Cached,
            DecisionRule = JevTermClassifier.HighlightRule,
            RankedAt = DateTime.UtcNow,
            TranscriptIds = candidate.TranscriptIds,
            MaterialIds = candidate.MaterialIds
        };
        var existing = await FindTermInsight(sessionId, insight.Id);
        if (existing is not null) return existing;
        try { await Collection<TermInsight>("term_insights").InsertOneAsync(insight); }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return (await FindTermInsight(sessionId, insight.Id))!;
        }
        Touch(sessionId, "insight", [insight.Id]);
        return insight;
    }
    public async Task<TermInsight> SaveTermExplanation(string sessionId, string insightId, GroundedResult result, bool detail = false)
    {
        var update = Builders<TermInsight>.Update
            .Set(x => x.Explanation, result.Answer)
            .Set(x => x.ExplanationVersion, GeminiLanguageModel.ExplanationPromptVersion)
            .Set(x => x.ExplanationSummary, result.Answer.Split("\n\n", StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(x => !x.StartsWith('#')) is { } summary
                ? summary[..Math.Min(summary.Length, 600)] : result.Answer[..Math.Min(result.Answer.Length, 600)])
            .Set(x => x.SearchQueries, result.SearchQueries ?? [])
            .Set(x => x.GroundingUsageJson, result.UsageJson)
            .Set(x => x.GroundingMetadataJson, result.GroundingMetadataJson)
            .Set(x => x.Evidence, result.Evidence.Select(x => new TermEvidence { Url = x.Url, Title = x.Title }).ToList())
            .Set(x => x.ExplainedAt, DateTime.UtcNow);
        if (detail && await FindTermInsight(sessionId, insightId) is { Explanation: not null } previous) {
            var archive = previous.ToBsonDocument(); archive["InsightId"] = insightId;
            archive["_id"] = ContentHash.Of(insightId + previous.ExplanationVersion + previous.Explanation);
            await Collection<BsonDocument>("term_explanation_history").ReplaceOneAsync(new BsonDocument("_id", archive["_id"]), archive, new ReplaceOptions { IsUpsert = true });
        }
        var saved = await Collection<TermInsight>("term_insights").FindOneAndUpdateAsync(
            x => x.SessionId == sessionId && x.Id == insightId && (x.Explanation == null || detail && x.ExplanationVersion != GeminiLanguageModel.ExplanationPromptVersion),
            update,
            new FindOneAndUpdateOptions<TermInsight> { ReturnDocument = ReturnDocument.After })
            ?? await FindTermInsight(sessionId, insightId)
            ?? throw new InvalidOperationException("Term insight not found");
        Touch(sessionId, "insight", [insightId]);
        return saved;
    }
    public Task<List<string>> TermReviewSessionIds() => Collection<SessionRecord>("sessions")
        .Find(FilterDefinition<SessionRecord>.Empty).SortByDescending(x => x.CreatedAt).Limit(128).Project(x => x.Id).ToListAsync();
}
