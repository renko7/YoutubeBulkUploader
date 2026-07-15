namespace YoutubeBulkUploader.Web.Models;

public enum UploadStatus
{
    Queued,
    Uploading,
    Completed,
    Failed
}

public enum VideoPrivacy
{
    Private,
    Unlisted,
    Public
}

public class UploadJob
{
    public int Id { get; set; }

    public required string FilePath { get; set; }

    public required string Title { get; set; }

    public string Description { get; set; } = string.Empty;

    public string Tags { get; set; } = string.Empty;

    public VideoPrivacy Privacy { get; set; } = VideoPrivacy.Private;

    public UploadStatus Status { get; set; } = UploadStatus.Queued;

    public double ProgressPercent { get; set; }

    public string? YouTubeVideoId { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    public string? PlaylistId { get; set; }

    public string? PlaylistTitle { get; set; }

    public int? PlaylistOrder { get; set; }

    public string? PlaylistItemId { get; set; }

    public string? PlaylistError { get; set; }
}
