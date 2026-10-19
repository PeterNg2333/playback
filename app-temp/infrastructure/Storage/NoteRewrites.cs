using MongoDB.Driver;
using Playback.Domain.Notes;

namespace Playback.Api.Db;

public partial class PlaybackStore
{
    public virtual Task<List<NoteRewriteProposal>> NoteRewrites(string id) => Collection<NoteRewriteProposal>("note_rewrites")
        .Find(x => x.SessionId == id && x.Status != "applied" && x.Status != "dismissed").SortByDescending(x => x.CreatedAt).Limit(20).ToListAsync();
    public virtual Task<NoteRewriteProposal?> NoteRewrite(string id, string proposalId) => Collection<NoteRewriteProposal>("note_rewrites")
        .Find(x => x.SessionId == id && x.Id == proposalId).FirstOrDefaultAsync()!;
    public virtual async Task SaveNoteRewrite(NoteRewriteProposal proposal)
    {
        await Collection<NoteRewriteProposal>("note_rewrites").ReplaceOneAsync(x => x.Id == proposal.Id && x.SessionId == proposal.SessionId, proposal, new ReplaceOptions {IsUpsert = true});
        Touch(proposal.SessionId, "processing");
    }
}
