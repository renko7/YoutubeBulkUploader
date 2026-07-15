using System.Text.Json;
using Google.Apis.Util.Store;
using Microsoft.EntityFrameworkCore;
using YoutubeBulkUploader.Web.Data;
using YoutubeBulkUploader.Web.Models;

namespace YoutubeBulkUploader.Web.Services;

/// <summary>
/// Persists Google.Apis.Auth token data (refresh/access tokens) in the app's SQLite database
/// instead of the library's default plaintext file store, encrypting each value with <see cref="SecretProtector"/>.
/// </summary>
public class EfCoreDataStore(IDbContextFactory<AppDbContext> dbContextFactory, SecretProtector protector) : IDataStore
{
    public async Task StoreAsync<T>(string key, T value)
    {
        var fullKey = BuildKey<T>(key);
        var json = JsonSerializer.Serialize(value);
        var protectedValue = protector.Protect(json);

        await using var db = await dbContextFactory.CreateDbContextAsync();
        var existing = await db.DataStoreEntries.FindAsync(fullKey);
        if (existing is null)
        {
            db.DataStoreEntries.Add(new DataStoreEntry { Key = fullKey, ValueProtected = protectedValue });
        }
        else
        {
            existing.ValueProtected = protectedValue;
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync<T>(string key)
    {
        var fullKey = BuildKey<T>(key);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var existing = await db.DataStoreEntries.FindAsync(fullKey);
        if (existing is not null)
        {
            db.DataStoreEntries.Remove(existing);
            await db.SaveChangesAsync();
        }
    }

    public async Task<T> GetAsync<T>(string key)
    {
        var fullKey = BuildKey<T>(key);
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var existing = await db.DataStoreEntries.FindAsync(fullKey);
        if (existing is null)
        {
            return default!;
        }

        var json = protector.Unprotect(existing.ValueProtected);
        return JsonSerializer.Deserialize<T>(json)!;
    }

    public async Task ClearAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        db.DataStoreEntries.RemoveRange(db.DataStoreEntries);
        await db.SaveChangesAsync();
    }

    private static string BuildKey<T>(string key) => $"{typeof(T).FullName}-{key}";
}
