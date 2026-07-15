using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YoutubeBulkUploader.Web.Data;
using YoutubeBulkUploader.Web.Models;

namespace YoutubeBulkUploader.Web.Services;

/// <summary>
/// Owns the upload queue and the rolling-24h rate limit bookkeeping. The DB (via
/// <see cref="AppDbContext"/>) is the single source of truth; this class is a thin,
/// thread-safe façade over it that also signals the background worker and the UI.
/// </summary>
public class UploadQueueManager(IDbContextFactory<AppDbContext> dbContextFactory, IOptions<UploadSettings> settings)
{
    private readonly UploadSettings _settings = settings.Value;
    private readonly SemaphoreSlim _wakeSignal = new(0, int.MaxValue);
    private readonly Lock _stateLock = new();
    private DateTime? _forcedCooldownUntilUtc;

    public bool IsPaused { get; private set; } = true;

    public event Action? OnChanged;

    public async Task<int> ScanFolderAsync(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Folder not found: {folderPath}");
        }

        var candidateFiles = Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
            .Where(f => _settings.VideoExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var existingPaths = await db.UploadJobs.Select(j => j.FilePath).ToListAsync();
        var existingSet = new HashSet<string>(existingPaths, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var file in candidateFiles)
        {
            if (existingSet.Contains(file))
            {
                continue;
            }

            db.UploadJobs.Add(new UploadJob
            {
                FilePath = file,
                Title = Path.GetFileNameWithoutExtension(file),
                Privacy = _settings.DefaultPrivacy,
                Status = UploadStatus.Queued
            });
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync();
            NotifyChanged();
            Wake();
        }

        return added;
    }

    public async Task<List<UploadJob>> GetSnapshotAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.UploadJobs.OrderBy(j => j.CreatedAtUtc).AsNoTracking().ToListAsync();
    }

    public async Task RemoveJobAsync(int id)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var job = await db.UploadJobs.FindAsync(id);
        if (job is not null && job.Status == UploadStatus.Queued)
        {
            db.UploadJobs.Remove(job);
            await db.SaveChangesAsync();
            NotifyChanged();
        }
    }

    public async Task RetryJobAsync(int id)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var job = await db.UploadJobs.FindAsync(id);
        if (job is not null && job.Status == UploadStatus.Failed)
        {
            job.Status = UploadStatus.Queued;
            job.ErrorMessage = null;
            job.ProgressPercent = 0;
            await db.SaveChangesAsync();
            NotifyChanged();
            Wake();
        }
    }

    public void Pause()
    {
        IsPaused = true;
        NotifyChanged();
    }

    public void Resume()
    {
        IsPaused = false;
        NotifyChanged();
        Wake();
    }

    public void Wake()
    {
        if (_wakeSignal.CurrentCount == 0)
        {
            _wakeSignal.Release();
        }
    }

    public async Task WaitForWakeAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _wakeSignal.WaitAsync(timeout, cancellationToken);
    }

    public async Task<RateLimitStatus> GetRateLimitStatusAsync()
    {
        var limit = _settings.DailyUploadLimit;
        var windowStart = DateTime.UtcNow.AddHours(-24);

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var recentCompletions = await db.UploadJobs
            .Where(j => j.Status == UploadStatus.Completed && j.CompletedAtUtc != null && j.CompletedAtUtc > windowStart)
            .Select(j => j.CompletedAtUtc!.Value)
            .OrderBy(t => t)
            .ToListAsync();

        DateTime? nextSlotFromWindow = recentCompletions.Count >= limit
            ? recentCompletions[recentCompletions.Count - limit].AddHours(24)
            : null;

        DateTime? forcedCooldown;
        lock (_stateLock)
        {
            if (_forcedCooldownUntilUtc is not null && _forcedCooldownUntilUtc <= DateTime.UtcNow)
            {
                _forcedCooldownUntilUtc = null;
            }

            forcedCooldown = _forcedCooldownUntilUtc;
        }

        var used = recentCompletions.Count;
        DateTime? nextSlotAtUtc = nextSlotFromWindow;
        if (forcedCooldown is not null && (nextSlotAtUtc is null || forcedCooldown > nextSlotAtUtc))
        {
            nextSlotAtUtc = forcedCooldown;
            used = Math.Max(used, limit);
        }

        return new RateLimitStatus(used, limit, nextSlotAtUtc);
    }

    public void RecordRateLimitHitNow()
    {
        lock (_stateLock)
        {
            _forcedCooldownUntilUtc = DateTime.UtcNow.AddHours(24);
        }

        NotifyChanged();
    }

    public async Task<UploadJob?> DequeueNextJobAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var job = await db.UploadJobs
            .Where(j => j.Status == UploadStatus.Queued)
            .OrderBy(j => j.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (job is null)
        {
            return null;
        }

        job.Status = UploadStatus.Uploading;
        job.ProgressPercent = 0;
        await db.SaveChangesAsync();
        NotifyChanged();
        return job;
    }

    public void ReportProgress(int jobId, double percent)
    {
        // Fire-and-forget, best-effort UI update; not critical if a write races with the next status change.
        _ = Task.Run(async () =>
        {
            await using var db = await dbContextFactory.CreateDbContextAsync();
            var job = await db.UploadJobs.FindAsync(jobId);
            if (job is not null && job.Status == UploadStatus.Uploading)
            {
                job.ProgressPercent = percent;
                await db.SaveChangesAsync();
                NotifyChanged();
            }
        });
    }

    public async Task MarkCompletedAsync(int jobId, string youTubeVideoId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var job = await db.UploadJobs.FindAsync(jobId);
        if (job is null)
        {
            return;
        }

        job.Status = UploadStatus.Completed;
        job.ProgressPercent = 100;
        job.YouTubeVideoId = youTubeVideoId;
        job.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        NotifyChanged();
    }

    public async Task MarkBackToQueuedAsync(int jobId, string reason)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var job = await db.UploadJobs.FindAsync(jobId);
        if (job is null)
        {
            return;
        }

        job.Status = UploadStatus.Queued;
        job.ProgressPercent = 0;
        job.ErrorMessage = reason;
        await db.SaveChangesAsync();
        NotifyChanged();
    }

    public async Task MarkFailedAsync(int jobId, string errorMessage)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var job = await db.UploadJobs.FindAsync(jobId);
        if (job is null)
        {
            return;
        }

        job.Status = UploadStatus.Failed;
        job.ErrorMessage = errorMessage;
        await db.SaveChangesAsync();
        NotifyChanged();
    }

    private void NotifyChanged() => OnChanged?.Invoke();
}
