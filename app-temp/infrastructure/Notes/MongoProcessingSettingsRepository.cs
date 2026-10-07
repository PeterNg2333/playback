using MongoDB.Driver;
using Playback.Application.Notes;

namespace Playback.Infrastructure.Notes;

public sealed class MongoProcessingSettingsRepository(IMongoCollection<ProcessingSettingsRecord> collection) : IProcessingSettingsRepository
{
    public Task<ProcessingSettingsRecord?> Read(string key) => collection.Find(x => x.Id == key).FirstOrDefaultAsync()!;
    public async Task Save(ProcessingSettingsRecord value, int expectedRevision)
    {
        if (expectedRevision == 0)
        {
            try { await collection.InsertOneAsync(value); }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            { throw new InvalidOperationException("Processing settings changed; reload before saving"); }
        }
        else
        {
            var result = await collection.ReplaceOneAsync(x => x.Id == value.Id && x.Revision == expectedRevision, value);
            if (result.MatchedCount != 1) throw new InvalidOperationException("Processing settings changed; reload before saving");
        }
    }
}
