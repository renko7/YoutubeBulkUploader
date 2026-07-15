using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using YoutubeBulkUploader.Web.Models;

namespace YoutubeBulkUploader.Web.Services;

public record VisibilityUpdateResult(string VideoId, bool Success, string? Error);

/// <summary>
/// Lists every video on the connected channel (not just ones this app uploaded) and
/// supports bulk privacy-status changes, for the "mass change visibility" feature.
/// </summary>
public class ChannelVideoService
{
    public async Task<List<Video>> GetAllChannelVideosAsync(UserCredential credential)
    {
        using var youtube = CreateClient(credential);

        var channelRequest = youtube.Channels.List("contentDetails");
        channelRequest.Mine = true;
        var channelResponse = await channelRequest.ExecuteAsync();
        var uploadsPlaylistId = channelResponse.Items?.FirstOrDefault()?.ContentDetails?.RelatedPlaylists?.Uploads;
        if (string.IsNullOrEmpty(uploadsPlaylistId))
        {
            return [];
        }

        var videoIds = new List<string>();
        string? pageToken = null;
        do
        {
            var itemsRequest = youtube.PlaylistItems.List("contentDetails");
            itemsRequest.PlaylistId = uploadsPlaylistId;
            itemsRequest.MaxResults = 50;
            itemsRequest.PageToken = pageToken;
            var itemsResponse = await itemsRequest.ExecuteAsync();

            videoIds.AddRange((itemsResponse.Items ?? [])
                .Select(i => i.ContentDetails?.VideoId)
                .Where(id => !string.IsNullOrEmpty(id))!);

            pageToken = itemsResponse.NextPageToken;
        } while (!string.IsNullOrEmpty(pageToken));

        var videos = new List<Video>();
        foreach (var batch in videoIds.Chunk(50))
        {
            var videosRequest = youtube.Videos.List("snippet,status");
            videosRequest.Id = string.Join(',', batch);
            var videosResponse = await videosRequest.ExecuteAsync();
            videos.AddRange(videosResponse.Items ?? []);
        }

        return videos;
    }

    public async Task<List<VisibilityUpdateResult>> UpdateVisibilityAsync(
        UserCredential credential,
        IEnumerable<Video> videos,
        VideoPrivacy newPrivacy)
    {
        using var youtube = CreateClient(credential);

        var results = new List<VisibilityUpdateResult>();
        foreach (var video in videos)
        {
            try
            {
                video.Status ??= new VideoStatus();
                video.Status.PrivacyStatus = newPrivacy.ToString().ToLowerInvariant();

                var request = youtube.Videos.Update(video, "status");
                await request.ExecuteAsync();
                results.Add(new VisibilityUpdateResult(video.Id, true, null));
            }
            catch (Exception ex)
            {
                results.Add(new VisibilityUpdateResult(video.Id, false, ex.Message));
            }

            await Task.Delay(200);
        }

        return results;
    }

    private static YouTubeService CreateClient(UserCredential credential) => new(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "YoutubeBulkUploader"
    });
}
