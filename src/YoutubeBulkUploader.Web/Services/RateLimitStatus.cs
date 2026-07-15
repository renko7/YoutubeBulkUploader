namespace YoutubeBulkUploader.Web.Services;

public record RateLimitStatus(int UsedInWindow, int Limit, DateTime? NextSlotAtUtc)
{
    public bool IsCapped => UsedInWindow >= Limit;
}
