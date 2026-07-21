namespace WTK.MediaForge.Remote.Signaling;

internal sealed class ExpiredSessionCleanupService(
    IRemoteSceneSessionStore store,
    TimeProvider timeProvider,
    ILogger<ExpiredSessionCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var deleted = await store.DeleteExpiredAsync(
                    timeProvider.GetUtcNow(),
                    stoppingToken).ConfigureAwait(false);
                if (deleted > 0)
                    logger.LogInformation("Removed {ExpiredSessionCount} expired Remote Scene signaling sessions.", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to remove expired Remote Scene signaling sessions.");
            }
        }
    }
}
