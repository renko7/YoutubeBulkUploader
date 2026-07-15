<#
.SYNOPSIS
Removes the YoutubeBulkUploader autostart Startup-folder launcher and stops
the running instance. Does not delete your data (SQLite DB / OAuth tokens
under %LOCALAPPDATA%\YoutubeBulkUploader) or the published app files.
#>

$ErrorActionPreference = "Stop"

$StartupDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup"
$LauncherPath = Join-Path $StartupDir "YoutubeBulkUploader.vbs"

Remove-Item -Path $LauncherPath -Force -ErrorAction SilentlyContinue

Get-Process -Name "powershell" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*run-loop.ps1*" } |
    Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name "YoutubeBulkUploader.Web" -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host "Autostart removed. The app won't start automatically anymore." -ForegroundColor Green
Write-Host "You can still run it manually with: dotnet run --project src/YoutubeBulkUploader.Web"
