namespace YoutubeBulkUploader.Web.Services;

/// <summary>
/// Drains the upload queue one video at a time, sleeping whenever there's nothing to do,
/// the queue is paused, no Google account is connected, or the rolling 24h/15-upload cap
/// (see <see cref="UploadQueueManager.GetRateLimitStatusAsync"/>) has been hit.
/// </summary>
public class UploadBackgroundWorker(
    UploadQueueManager queueManager,
    GoogleAuthService authService,
    YouTubeUploadService uploadService,
    PlaylistService playlistService,
    ILogger<UploadBackgroundWorker> logger) : BackgroundService
{
    private static readonly TimeSpan IdlePoll = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MaxBackoffCheck = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (queueManager.IsPaused)
                {
                    await queueManager.WaitForWakeAsync(IdlePoll, stoppingToken);
                    continue;
                }

                var credential = await authService.GetCredentialAsync();
                if (credential is null)
                {
                    await queueManager.WaitForWakeAsync(IdlePoll, stoppingToken);
                    continue;
                }

                await ProcessPendingPlaylistAddsAsync(credential, stoppingToken);

                var rateStatus = await queueManager.GetRateLimitStatusAsync();
                if (rateStatus.IsCapped && rateStatus.NextSlotAtUtc is { } nextSlot)
                {
                    var wait = nextSlot - DateTime.UtcNow;
                    if (wait > TimeSpan.Zero)
                    {
                        var sleepFor = wait < MaxBackoffCheck ? wait : MaxBackoffCheck;
                        await queueManager.WaitForWakeAsync(sleepFor, stoppingToken);
                        continue;
                    }
                }

                var job = await queueManager.DequeueNextJobAsync();
                if (job is null)
                {
                    await queueManager.WaitForWakeAsync(IdlePoll, stoppingToken);
                    continue;
                }

                var progress = new Progress<double>(p => queueManager.ReportProgress(job.Id, p));

                var result = await uploadService.UploadAsync(credential, job, progress, stoppingToken);

                if (result.Success)
                {
                    await queueManager.MarkCompletedAsync(job.Id, result.YouTubeVideoId!);
                }
                else if (result.RateLimited)
                {
                    queueManager.RecordRateLimitHitNow();
                    await queueManager.MarkBackToQueuedAsync(job.Id, "Paused: YouTube's upload limit was reached. Will resume automatically after the cooldown.");
                }
                else
                {
                    await queueManager.MarkFailedAsync(job.Id, result.ErrorMessage ?? "Unknown upload error.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in upload worker loop");
                await Task.Delay(IdlePoll, stoppingToken);
            }
        }
    }

    private async Task ProcessPendingPlaylistAddsAsync(
        Google.Apis.Auth.OAuth2.UserCredential credential,
        CancellationToken stoppingToken)
    {
        var pending = await queueManager.GetPendingPlaylistAddsAsync();
        foreach (var job in pending)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var position = await queueManager.CountAddedBeforeOrderAsync(job.PlaylistId!, job.PlaylistOrder);
                var itemId = await playlistService.AddVideoToPlaylistAsync(credential, job.PlaylistId!, job.YouTubeVideoId!, position);
                await queueManager.MarkPlaylistItemAddedAsync(job.Id, itemId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to add job {JobId} to playlist {PlaylistId}", job.Id, job.PlaylistId);
                await queueManager.MarkPlaylistErrorAsync(job.Id, ex.Message);
            }
        }
    }
}
