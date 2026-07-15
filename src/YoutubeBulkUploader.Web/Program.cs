using Microsoft.EntityFrameworkCore;
using YoutubeBulkUploader.Web.Components;
using YoutubeBulkUploader.Web.Data;
using YoutubeBulkUploader.Web.Models;
using YoutubeBulkUploader.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Fixed ports regardless of how the app is launched (dotnet run, or the published
// standalone exe for autostart), so the OAuth redirect URI registered in Google Cloud
// Console (https://localhost:7080/oauth/callback) always matches.
builder.WebHost.UseUrls("https://localhost:7080", "http://localhost:5196");

var dataDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "YoutubeBulkUploader");
Directory.CreateDirectory(dataDirectory);
var dbPath = Path.Combine(dataDirectory, "data.db");

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<UploadSettings>(builder.Configuration.GetSection("UploadSettings"));

builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddSingleton<SecretProtector>();
builder.Services.AddSingleton<EfCoreDataStore>();
builder.Services.AddSingleton<GoogleAuthService>();
builder.Services.AddSingleton<YouTubeUploadService>();
builder.Services.AddSingleton<PlaylistService>();
builder.Services.AddSingleton<ChannelVideoService>();
builder.Services.AddSingleton<UploadQueueManager>();
builder.Services.AddHostedService<UploadBackgroundWorker>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await dbContextFactory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/oauth/callback", async (HttpContext context, GoogleAuthService authService) =>
{
    var code = context.Request.Query["code"].ToString();
    var error = context.Request.Query["error"].ToString();

    if (!string.IsNullOrEmpty(error))
    {
        return Results.Redirect($"/setup?error={Uri.EscapeDataString(error)}");
    }

    if (string.IsNullOrEmpty(code))
    {
        return Results.Redirect("/setup?error=missing_code");
    }

    var redirectUri = $"{context.Request.Scheme}://{context.Request.Host}/oauth/callback";
    var success = await authService.HandleCallbackAsync(code, redirectUri);
    return Results.Redirect(success ? "/setup?connected=1" : "/setup?error=exchange_failed");
});

app.Run();
