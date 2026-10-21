using Playback.Domain.Models;
namespace Playback.Application.Translation;
public interface ITranslationRepository
{
    Task<List<Transcript>> PendingTranslations(int limit = 30, string? sessionId = null);
    Task<SessionView?> Session(string id);
    Task<Transcript> SetTranslationResult(Transcript transcript, string language, string? value, string? error);
}
