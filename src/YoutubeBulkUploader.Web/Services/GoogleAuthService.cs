using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Microsoft.EntityFrameworkCore;
using YoutubeBulkUploader.Web.Data;
using YoutubeBulkUploader.Web.Models;

namespace YoutubeBulkUploader.Web.Services;

/// <summary>
/// Manages the Google OAuth "web application" flow for a single local user:
/// storing the user-supplied client id/secret, building the consent URL, exchanging
/// the callback code for tokens, and handing back a refreshing UserCredential for API calls.
/// </summary>
public class GoogleAuthService(
    IDbContextFactory<AppDbContext> dbContextFactory,
    SecretProtector protector,
    EfCoreDataStore dataStore)
{
    public const string LocalUserId = "local-user";

    private static readonly string[] Scopes =
    [
        YouTubeService.Scope.YoutubeUpload,
        YouTubeService.Scope.YoutubeReadonly
    ];

    public async Task<(string ClientId, string ClientSecret)?> GetClientConfigAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var config = await db.OAuthClientConfigs.FindAsync(1);
        if (config is null)
        {
            return null;
        }

        return (protector.Unprotect(config.ClientIdProtected), protector.Unprotect(config.ClientSecretProtected));
    }

    public async Task<string?> GetConnectedChannelTitleAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var config = await db.OAuthClientConfigs.FindAsync(1);
        return config?.ConnectedChannelTitle;
    }

    public async Task SaveClientConfigAsync(string clientId, string clientSecret)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var config = await db.OAuthClientConfigs.FindAsync(1);
        var idProtected = protector.Protect(clientId);
        var secretProtected = protector.Protect(clientSecret);

        if (config is null)
        {
            db.OAuthClientConfigs.Add(new OAuthClientConfig
            {
                Id = 1,
                ClientIdProtected = idProtected,
                ClientSecretProtected = secretProtected
            });
        }
        else
        {
            config.ClientIdProtected = idProtected;
            config.ClientSecretProtected = secretProtected;
        }

        await db.SaveChangesAsync();
    }

    public async Task<GoogleAuthorizationCodeFlow?> BuildFlowAsync()
    {
        var clientConfig = await GetClientConfigAsync();
        if (clientConfig is null)
        {
            return null;
        }

        return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = clientConfig.Value.ClientId,
                ClientSecret = clientConfig.Value.ClientSecret
            },
            Scopes = Scopes,
            DataStore = dataStore
        });
    }

    public async Task<string?> BuildConsentUrlAsync(string redirectUri)
    {
        var flow = await BuildFlowAsync();
        if (flow is null)
        {
            return null;
        }

        var request = (Google.Apis.Auth.OAuth2.Requests.GoogleAuthorizationCodeRequestUrl)flow.CreateAuthorizationCodeRequest(redirectUri);
        request.AccessType = "offline";
        request.Prompt = "consent";
        return request.Build().ToString();
    }

    public async Task<bool> HandleCallbackAsync(string code, string redirectUri)
    {
        var flow = await BuildFlowAsync();
        if (flow is null)
        {
            return false;
        }

        TokenResponse token = await flow.ExchangeCodeForTokenAsync(LocalUserId, code, redirectUri, CancellationToken.None);
        var credential = new UserCredential(flow, LocalUserId, token);

        using var youtube = new YouTubeService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "YoutubeBulkUploader"
        });

        var channelsRequest = youtube.Channels.List("snippet");
        channelsRequest.Mine = true;
        var channelsResponse = await channelsRequest.ExecuteAsync();
        var channelTitle = channelsResponse.Items?.FirstOrDefault()?.Snippet?.Title;

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var config = await db.OAuthClientConfigs.FindAsync(1);
        if (config is not null)
        {
            config.ConnectedChannelTitle = channelTitle ?? "Connected (channel name unavailable)";
            await db.SaveChangesAsync();
        }

        return true;
    }

    public async Task<UserCredential?> GetCredentialAsync()
    {
        var flow = await BuildFlowAsync();
        if (flow is null)
        {
            return null;
        }

        var token = await dataStore.GetAsync<TokenResponse>(LocalUserId);
        if (token is null)
        {
            return null;
        }

        return new UserCredential(flow, LocalUserId, token);
    }

    public async Task DisconnectAsync()
    {
        await dataStore.DeleteAsync<TokenResponse>(LocalUserId);

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var config = await db.OAuthClientConfigs.FindAsync(1);
        if (config is not null)
        {
            config.ConnectedChannelTitle = null;
            await db.SaveChangesAsync();
        }
    }
}
