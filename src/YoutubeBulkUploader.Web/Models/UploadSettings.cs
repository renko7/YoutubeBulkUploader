namespace YoutubeBulkUploader.Web.Models;

public class UploadSettings
{
    public int DailyUploadLimit { get; set; } = 15;

    public VideoPrivacy DefaultPrivacy { get; set; } = VideoPrivacy.Private;

    public string[] VideoExtensions { get; set; } =
        [".mp4", ".mov", ".avi", ".wmv", ".mkv", ".webm", ".m4v", ".mpg", ".mpeg", ".flv"];
}
