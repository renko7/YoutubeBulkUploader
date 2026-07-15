using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using YoutubeBulkUploader.Web.Models;

namespace YoutubeBulkUploader.Web.Services;

public record PlaylistSummary(string Id, string Title);

/// <summary>
/// Thin wrapper around the Playlists/PlaylistItems API surface: listing the user's
/// playlists, creating new ones, and adding a video to a playlist at a given position.
/// </summary>
public class PlaylistService
{
    public async Task<List<PlaylistSummary>> GetPlaylistsAsync(UserCredential credential)
    {
        using var youtube = CreateClient(credential);

        var results = new List<PlaylistSummary>();
        string? pageToken = null;
        do
        {
            var request = youtube.Playlists.List("snippet");
            request.Mine = true;
            request.MaxResults = 50;
            request.PageToken = pageToken;
            var response = await request.ExecuteAsync();

            results.AddRange((response.Items ?? [])
                .Select(p => new PlaylistSummary(p.Id, p.Snippet?.Title ?? "(untitled)")));

            pageToken = response.NextPageToken;
        } while (!string.IsNullOrEmpty(pageToken));

        return results;
    }

    public async Task<PlaylistSummary> CreatePlaylistAsync(UserCredential credential, string title, VideoPrivacy privacy)
    {
        using var youtube = CreateClient(credential);

        var playlist = new Playlist
        {
            Snippet = new PlaylistSnippet { Title = title },
            Status = new PlaylistStatus { PrivacyStatus = privacy.ToString().ToLowerInvariant() }
        };

        var request = youtube.Playlists.Insert(playlist, "snippet,status");
        var created = await request.ExecuteAsync();
        return new PlaylistSummary(created.Id, created.Snippet?.Title ?? title);
    }

    public async Task<string> AddVideoToPlaylistAsync(UserCredential credential, string playlistId, string videoId, int position)
    {
        using var youtube = CreateClient(credential);

        var playlistItem = new PlaylistItem
        {
            Snippet = new PlaylistItemSnippet
            {
                PlaylistId = playlistId,
                Position = position,
                ResourceId = new ResourceId { Kind = "youtube#video", VideoId = videoId }
            }
        };

        var request = youtube.PlaylistItems.Insert(playlistItem, "snippet");
        var created = await request.ExecuteAsync();
        return created.Id;
    }

    private static YouTubeService CreateClient(UserCredential credential) => new(new BaseClientService.Initializer
    {
        HttpClientInitializer = credential,
        ApplicationName = "YoutubeBulkUploader"
    });
}
