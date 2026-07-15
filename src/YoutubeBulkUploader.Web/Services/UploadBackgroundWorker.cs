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
}
