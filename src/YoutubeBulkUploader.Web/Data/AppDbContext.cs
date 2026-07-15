using Microsoft.EntityFrameworkCore;
using YoutubeBulkUploader.Web.Models;

namespace YoutubeBulkUploader.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UploadJob> UploadJobs => Set<UploadJob>();

    public DbSet<OAuthClientConfig> OAuthClientConfigs => Set<OAuthClientConfig>();

    public DbSet<DataStoreEntry> DataStoreEntries => Set<DataStoreEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DataStoreEntry>().HasKey(e => e.Key);
    }
}
