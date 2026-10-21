using Playback.Domain.Assistance;
using Playback.Domain.Evidence;
using Playback.Domain.Models;
using Playback.Domain.Workspace;
using MongoDB.Driver;
using MongoDB.Bson.Serialization.Attributes;

namespace Playback.Infrastructure.Storage;

public partial class PlaybackStore
{
    public async Task<object> CreateSession(string title, string? groupId = null)
    {
        title = WorkspaceRules.Title(title);
        if (groupId is not null) await RequireGroupOwner(groupId);
        if (groupId is not null && await Collection<GroupRecord>("groups").CountDocumentsAsync(x => x.Id == groupId) == 0)
            throw new InvalidOperationException("Group not found");
        var item = new SessionRecord { Id = Guid.NewGuid().ToString("N"), OwnerId = CurrentOwner, Title = title, GroupId = groupId, CreatedAt = DateTime.UtcNow };
        await Collection<SessionRecord>("sessions").InsertOneAsync(item);
        return new { item.Id, item.Title, item.GroupId };
    }
    public async Task<List<SessionRecord>> Sessions() =>
        await Collection<SessionRecord>("sessions")
            .Find(CurrentOwner is { } owner ? Builders<SessionRecord>.Filter.Eq(x => x.OwnerId, owner) : FilterDefinition<SessionRecord>.Empty)
            .SortByDescending(x => x.CreatedAt)
            .Limit(200)
            .ToListAsync();
    public async Task<List<GroupRecord>> Groups() =>
        await Collection<GroupRecord>("groups")
            .Find(CurrentOwner is { } owner ? Builders<GroupRecord>.Filter.Eq(x => x.OwnerId, owner) : FilterDefinition<GroupRecord>.Empty)
            .SortBy(x => x.CreatedAt)
            .ToListAsync();
    public async Task<GroupRecord> CreateGroup(string name)
    {
        name = WorkspaceRules.GroupName(name);
        var group = new GroupRecord { Id = Guid.NewGuid().ToString("N"), OwnerId = CurrentOwner, Name = name, CreatedAt = DateTime.UtcNow };
        await Collection<GroupRecord>("groups").InsertOneAsync(group);
        return group;
    }
    public async Task<GroupRecord> RenameGroup(string id, string name)
    {
        await RequireGroupOwner(id);
        name = WorkspaceRules.GroupName(name);
        return await Collection<GroupRecord>("groups").FindOneAndUpdateAsync(x => x.Id == id,
            Builders<GroupRecord>.Update.Set(x => x.Name, name),
            new FindOneAndUpdateOptions<GroupRecord> { ReturnDocument = ReturnDocument.After })
            ?? throw new InvalidOperationException("Group not found");
    }
    public async Task DeleteGroup(string id)
    {
        await RequireGroupOwner(id);
        var members = await Collection<SessionRecord>("sessions").Find(x => x.GroupId == id).Project(x => x.Id).ToListAsync();
        var deleted = await Collection<GroupRecord>("groups").DeleteOneAsync(x => x.Id == id);
        var moved = await Collection<SessionRecord>("sessions").UpdateManyAsync(
            x => x.GroupId == id,
            Builders<SessionRecord>.Update.Set(x => x.GroupId, null));
        if (deleted.DeletedCount == 0 && moved.MatchedCount == 0)
            throw new InvalidOperationException("Group not found");
        foreach (var member in members) Touch(member, "group");
    }
    public async Task<object> MoveSession(string id, string? groupId)
    {
        await RequireSessionOwner(id);
        if (groupId is not null) await RequireGroupOwner(groupId);
        if (groupId is not null && await Collection<GroupRecord>("groups").CountDocumentsAsync(x => x.Id == groupId) == 0)
            throw new InvalidOperationException("Group not found");
        var result = await Collection<SessionRecord>("sessions").UpdateOneAsync(x => x.Id == id,
            Builders<SessionRecord>.Update.Set(x => x.GroupId, groupId));
        if (result.MatchedCount == 0) throw new InvalidOperationException("Session not found");
        Touch(id, "group");
        return new { id, groupId };
    }
    public async Task<SessionRecord> SessionSettings(string id) =>
        await Collection<SessionRecord>("sessions").Find(x => x.Id == id).FirstOrDefaultAsync()
        ?? throw new InvalidOperationException("Session not found");
    public async Task<SessionRecord> SetLanguages(string id, string asrLanguage, string noteLanguage, string? asrModel = null)
    {
        LanguageSettings.ValidateAsr(asrLanguage);
        LanguageSettings.ValidateNote(noteLanguage);
        AsrModelOptions.Validate(asrModel);
        var writeLock = noteWriteLocks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
        await writeLock.WaitAsync();
        try
        {
            var result = await Collection<SessionRecord>("sessions").FindOneAndUpdateAsync(x => x.Id == id,
                Builders<SessionRecord>.Update.Set(x => x.AsrLanguage, asrLanguage).Set(x => x.NoteLanguage, noteLanguage).Set(x => x.AsrModel, asrModel),
                new FindOneAndUpdateOptions<SessionRecord> { ReturnDocument = ReturnDocument.After })
                ?? throw new InvalidOperationException("Session not found");
            Touch(id, "display-settings");
            return result;
        }
        finally { writeLock.Release(); }
    }
    public virtual async Task<SessionView?> Session(string id)
    {
        var session = await Collection<SessionRecord>("sessions").Find(x => x.Id == id).FirstOrDefaultAsync();
        if (session is null) return null;
        var materials = await Collection<Material>("materials").Find(x => x.SessionId == id).ToListAsync();
        var rows = await Collection<Transcript>("transcripts").Find(x => x.SessionId == id).SortBy(x => x.StartMs).ToListAsync();
        var supersessions = Lineage.Supersessions(rows);
        var transcripts = rows.Where(x => x.Active).ToList();
        // Recompute display from immutable provider text, including older transcripts. No ASR call.
        foreach (var transcript in transcripts)
            transcript.DisplayOriginal = LanguageSettings.CantoneseDisplay(transcript.Original, session.AsrLanguage, transcript.AsrDetectedLanguage);
        var chunks = await Collection<ChunkRecord>("chunks").Find(x => x.SessionId == id).SortBy(x => x.StartMs).ToListAsync();
        var note = await NoteRepository.Latest(id) is { } latest ? Lineage.Apply(latest, supersessions) : null;
        var insights = await Collection<TermInsight>("term_insights").Find(x => x.SessionId == id).ToListAsync();
        foreach (var insight in insights) insight.TranscriptIds = Lineage.Remap(insight.TranscriptIds, supersessions);
        var terms = TermCandidateExtractor.Find(materials, transcripts);
        foreach (var insight in insights)
        {
            var candidate = terms.FirstOrDefault(x => TermInsight.IdFor(id, x.Text, x.Context, session.NoteLanguage) == insight.Id);
            if (candidate is null) continue;
            insight.TranscriptIds = insight.TranscriptIds.Concat(candidate.TranscriptIds).Distinct().ToList();
            insight.MaterialIds = insight.MaterialIds.Concat(candidate.MaterialIds).Distinct().ToList();
        }
        return new SessionView(
            session.Id,
            session.Title,
            session.GroupId,
            session.CreatedAt,
            note?.Markdown ?? "",
            note?.Version ?? 0,
            session.TranslationEnabled,
            session.TranslationLanguage ?? "zh-Hant",
            materials,
            transcripts,
            chunks,
            terms,
            insights,
            note,
            session.AsrLanguage,
            session.NoteLanguage, session.AsrModel) { Supersessions = supersessions };
    }
    public async Task<object> AddMaterial(string id, string name, string text)
    {
        WorkspaceRules.Material(name, text);
        if (await Collection<SessionRecord>("sessions").CountDocumentsAsync(x => x.Id == id) == 0)
            throw new InvalidOperationException("Session not found");
        var material = new Material { Id = Guid.NewGuid().ToString("N"), SessionId = id, Name = name, Text = text };
        await Collection<Material>("materials").InsertOneAsync(material);
        Touch(id, "material", [material.Id]);
        SourceChanged?.Invoke(id);
        return new { material.Id, material.Name };
    }
}
