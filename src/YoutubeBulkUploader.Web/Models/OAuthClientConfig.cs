namespace YoutubeBulkUploader.Web.Models;

/// <summary>
/// Singleton row (Id is always 1) holding the user-supplied Google OAuth client id/secret.
/// Values are DPAPI-encrypted at rest by <see cref="Services.SecretProtector"/> before being stored.
/// </summary>
public class OAuthClientConfig
{
    public int Id { get; set; } = 1;

    public required byte[] ClientIdProtected { get; set; }

    public required byte[] ClientSecretProtected { get; set; }

    public string? ConnectedChannelTitle { get; set; }
}

/// <summary>
/// Backing store for Google.Apis.Auth's IDataStore (refresh/access tokens), encrypted at rest.
/// </summary>
public class DataStoreEntry
{
    public required string Key { get; set; }

    public required byte[] ValueProtected { get; set; }
}
