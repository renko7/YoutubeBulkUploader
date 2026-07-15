<#
.SYNOPSIS
Publishes YoutubeBulkUploader as a standalone exe and sets it up to start
automatically and invisibly whenever you log into Windows, restarting itself
if it ever crashes, so you never have to manually run `dotnet run` again.

Uses the Startup folder (not Task Scheduler, which this machine's policy
blocks non-admin users from registering new tasks with).

Re-run this script any time after pulling new code to update the installed
version (it republishes and restarts the running instance).
#>

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot "src\YoutubeBulkUploader.Web"
$InstallDir = Join-Path $env:LOCALAPPDATA "Programs\YoutubeBulkUploader"
$ExePath = Join-Path $InstallDir "YoutubeBulkUploader.Web.exe"
$RunLoopPath = Join-Path $InstallDir "run-loop.ps1"
$StartupDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup"
$LauncherPath = Join-Path $StartupDir "YoutubeBulkUploader.vbs"

Write-Host "Stopping any running instance ..."
Get-Process -Name "powershell" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*run-loop.ps1*" } |
    Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name "YoutubeBulkUploader.Web" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

Write-Host "Publishing to $InstallDir ..."
dotnet publish $Project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $InstallDir

if (-not (Test-Path $ExePath)) {
    throw "Publish did not produce $ExePath"
}

Write-Host "Writing supervisor loop ..."
@"
# Restarts YoutubeBulkUploader.Web.exe automatically if it ever exits/crashes.
`$exe = Join-Path `$PSScriptRoot "YoutubeBulkUploader.Web.exe"
while (`$true) {
    `$proc = Start-Process -FilePath `$exe -WindowStyle Hidden -PassThru
    `$proc.WaitForExit()
    Start-Sleep -Seconds 5
}
"@ | Set-Content -Path $RunLoopPath -Encoding UTF8

Write-Host "Registering Startup folder launcher ..."
New-Item -ItemType Directory -Path $StartupDir -Force | Out-Null
@"
CreateObject("WScript.Shell").Run "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ""$RunLoopPath""", 0, False
"@ | Set-Content -Path $LauncherPath -Encoding ASCII

Write-Host "Starting it now ..."
Start-Process -FilePath "wscript.exe" -ArgumentList "`"$LauncherPath`""
Start-Sleep -Seconds 3

Write-Host ""
Write-Host "Done. The app now starts automatically (invisibly) whenever you log in," -ForegroundColor Green
Write-Host "and restarts itself if it ever stops. Open https://localhost:7080 any time." -ForegroundColor Green
