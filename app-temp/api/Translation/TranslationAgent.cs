using Playback.Api.Db;
using Playback.Api.Providers;
using Playback.Application.Activity;
using Playback.Application.Translation;
using Playback.Infrastructure.Activity;
namespace Playback.Api.Translation;
// The host owns the timer/cancellation; Application owns batching, validation and result persistence.
public sealed class TranslationAgent : BackgroundService, IAsyncDisposable
{
    readonly TranslationWorkflow workflow; readonly ILogger<TranslationAgent> logger; readonly CancellationTokenSource stopping = new(); int disposed;
    public TranslationAgent(PlaybackStore store, GeminiLanguageModel gemini, ILogger<TranslationAgent> logger, AiActivity activity)
    {this.logger = logger; workflow = new(store, gemini, new WorkLog<TranslationAgent>(logger), activity);}
    public static string Instructions(string language) => TranslationWorkflow.Instructions(language);
    public static Dictionary<string,string> ParseBatch(string response, IReadOnlyCollection<string> ids) => TranslationWorkflow.ParseBatch(response, ids);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stoppingToken.Register(stopping.Cancel); if (PlaybackEnvironment.Offline) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try {do {try {await workflow.Scan(stopping.Token, PlaybackEnvironment.AllowsAutomaticWork);} catch (Exception ex) when (!stopping.IsCancellationRequested) {logger.LogWarning("Translation scan failed: {Error}", AiActivity.SafeError(ex));}}
            while (await timer.WaitForNextTickAsync(stopping.Token));}
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync()
    {if (Interlocked.Exchange(ref disposed, 1) != 0) return; stopping.Cancel(); await StopAsync(CancellationToken.None); stopping.Dispose(); base.Dispose();}
}
