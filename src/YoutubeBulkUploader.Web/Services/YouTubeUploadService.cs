using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using YoutubeBulkUploader.Web.Models;

namespace YoutubeBulkUploader.Web.Services;

public class VideoUploadResult
{
    public bool Success { get; init; }
    public bool RateLimited { get; init; }
    public string? YouTubeVideoId { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Wraps the YouTube Data API v3 resumable upload for a single video, reporting progress
/// and classifying failures so the caller can tell a real error apart from YouTube's
/// own upload-limit rejection (which should trigger a backoff rather than marking the job Failed).
/// </summary>
public class YouTubeUploadService
{
    public async Task<VideoUploadResult> UploadAsync(
        UserCredential credential,
        UploadJob job,
        IProgress<double> progress,
        CancellationToken cancellationToken)
    {
        using var youtubeService = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "YoutubeBulkUploader"
        });

        var video = new Video
        {
            Snippet = new VideoSnippet
            {
                Title = job.Title,
                Description = job.Description,
                Tags = string.IsNullOrWhiteSpace(job.Tags)
                    ? null
                    : job.Tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            },
            Status = new VideoStatus
            {
                PrivacyStatus = job.Privacy.ToString().ToLowerInvariant()
            }
        };

        await using var fileStream = new FileStream(job.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var insertRequest = youtubeService.Videos.Insert(video, "snippet,status", fileStream, "video/*");

        insertRequest.ProgressChanged += uploadProgress =>
        {
            if (fileStream.Length > 0)
            {
                progress.Report((double)uploadProgress.BytesSent / fileStream.Length * 100);
            }
        };

        IUploadProgress result;
        try
        {
            result = await insertRequest.UploadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Classify(ex);
        }

        if (result.Status == Google.Apis.Upload.UploadStatus.Completed)
        {
            return new VideoUploadResult { Success = true, YouTubeVideoId = insertRequest.ResponseBody?.Id };
        }

        return result.Exception is not null
            ? Classify(result.Exception)
            : new VideoUploadResult { Success = false, ErrorMessage = "Upload did not complete for an unknown reason." };
    }

    private static VideoUploadResult Classify(Exception ex)
    {
        if (ex is GoogleApiException googleEx)
        {
            var reasons = googleEx.Error?.Errors?.Select(e => e.Reason).ToList() ?? [];
            var isRateLimited = reasons.Any(r =>
                r is "uploadLimitExceeded" or "dailyLimitExceeded" or "quotaExceeded" or "rateLimitExceeded");

            if (isRateLimited)
            {
                return new VideoUploadResult
                {
                    Success = false,
                    RateLimited = true,
                    ErrorMessage = googleEx.Message
                };
            }
        }

        return new VideoUploadResult { Success = false, ErrorMessage = ex.Message };
    }
}
