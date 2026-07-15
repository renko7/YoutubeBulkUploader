# YouTube Bulk Uploader

A local Blazor Web App (.NET 10, server-rendered) that bulk-uploads a folder of videos to
YouTube via the YouTube Data API v3, automatically respecting YouTube's real-world upload
limit of roughly **15 uploads per rolling 24 hours**. Point it at a folder, hit start, and it
will keep uploading until it hits the cap, then wait and resume automatically — no need to
babysit it. It can also assign uploaded videos to a playlist in order, and bulk-change the
visibility (Private/Unlisted/Public) of any video on your channel.

> **Upgrading from an earlier version?** This version requests a wider Google OAuth scope
> (needed for playlist management and full channel video access). Go to **Setup** and click
> **Disconnect**, then **Connect Google Account** again — Google won't silently upgrade an
> already-granted token's scope.

## How it works

- You give the app a local folder path; it scans for video files and queues them.
- A background worker uploads one video at a time via the YouTube Data API's resumable upload.
- It tracks how many uploads completed in the last 24 hours. Once that count hits the
  configured limit (default 15), it pauses and automatically resumes once the oldest upload
  in that rolling window ages out past 24 hours.
- If YouTube itself rejects an upload with an upload/quota-limit error, the app treats that as
  authoritative and backs off for 24 hours, even if its own counter disagreed.
- The queue, job history, and your Google auth tokens persist in a local SQLite database
  (`%LOCALAPPDATA%\YoutubeBulkUploader\data.db`) and a Google OAuth client secret you provide —
  none of this is committed to the repo (see `.gitignore`).

## One-time setup: Google Cloud OAuth client

This app needs its own Google Cloud OAuth client so it can upload to your channel on your
behalf. Google doesn't allow apps to ship a shared client for this, so you create your own
(free, takes a few minutes):

1. Go to [Google Cloud Console → New Project](https://console.cloud.google.com/projectcreate) and create a project.
2. In that project, open [APIs & Services → Library → YouTube Data API v3](https://console.cloud.google.com/apis/library/youtube.googleapis.com) and click **Enable**.
3. Open [APIs & Services → OAuth consent screen](https://console.cloud.google.com/auth/overview).
   Choose **External**, fill in the app name/support email, and under **Audience → Test users**
   add your own Google account email (required while the app is unpublished/in testing).
4. Open [APIs & Services → Credentials → Create Credentials → OAuth client ID](https://console.cloud.google.com/auth/clients).
   Application type: **Web application**. Under **Authorized redirect URIs**, add:
   `https://localhost:7080/oauth/callback` (or whatever port the app runs on locally).
5. Copy the **Client ID** and **Client secret**.

Then run the app, go to the **Setup** page, paste in the Client ID/secret, and click
**Connect Google Account** to complete the OAuth flow.

## Running

```
dotnet run --project src/YoutubeBulkUploader.Web
```

Open the printed `https://localhost:...` URL, go to **Setup** to connect your account, then
**Upload Queue** to point it at a folder and start uploading.

## Running automatically (no manual start needed)

`scripts\install-autostart.ps1` publishes the app as a standalone exe and sets it up to start
invisibly whenever you log into Windows, restarting itself if it ever stops:

```
powershell -ExecutionPolicy Bypass -File .\scripts\install-autostart.ps1
```

It always listens on `https://localhost:7080` — just open that URL any time, no need to start
anything yourself. Re-run the script after pulling new code to update the running version.

This uses the Startup folder (`shell:startup`) rather than Task Scheduler, since some machines'
policies block non-admin users from registering new scheduled tasks. To remove autostart:

```
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall-autostart.ps1
```

## Configuration

`src/YoutubeBulkUploader.Web/appsettings.json`:

- `UploadSettings:DailyUploadLimit` — uploads allowed per rolling 24h window (default 15).
- `UploadSettings:DefaultPrivacy` — `Private`, `Unlisted`, or `Public` for newly queued videos.

## Project layout

```
src/YoutubeBulkUploader.Web/
  Components/Pages/Setup.razor       OAuth setup walkthrough + connect/disconnect
  Components/Pages/UploadQueue.razor Folder scan, queue table, playlist assignment, rate-limit status, start/pause
  Components/Pages/MyVideos.razor    Bulk visibility (Private/Unlisted/Public) changes for any channel video
  Services/GoogleAuthService.cs      OAuth2 web flow, token persistence
  Services/YouTubeUploadService.cs   Resumable video upload + progress reporting
  Services/PlaylistService.cs        Playlist listing/creation, adding videos to a playlist at a position
  Services/ChannelVideoService.cs    Full channel video listing + bulk privacy updates
  Services/PlaylistOrderParser.cs    Parses a leading number out of a filename for playlist ordering
  Services/UploadQueueManager.cs     Queue state + rolling 24h rate-limit bookkeeping + playlist assignment
  Services/UploadBackgroundWorker.cs Background loop that drains the queue and pending playlist adds
  Data/AppDbContext.cs               EF Core / SQLite persistence
```

## Playlist organization & mass visibility

- On the **Upload Queue** page, select one or more queued/uploaded videos, pick an existing
  playlist (or create a new one), and click **Assign Selected to Playlist**. If a filename
  starts with a number (`01 - Intro.mp4`, `2. Chapter Two.mp4`), that number is used as the
  playlist order automatically; otherwise edit the **Order** column yourself. Videos are added
  to the playlist as each upload finishes (or immediately, if already uploaded), independent of
  the 15/24h upload cap.
- The **My Videos** page lists every video on your connected channel (not just ones uploaded
  through this app) and lets you bulk-change their Private/Unlisted/Public status.
