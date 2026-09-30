<#
  Downloads the newest Ghost Watchers Trainer from the GitHub release and installs it.
  The repository is read from repo.txt ("owner/name") next to this script.
  Used by "Update Trainer.bat" and by Install.ps1 (-DownloadOnly).
#>
param([switch]$DownloadOnly, [string]$GamePath)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$repoFile = Join-Path $here 'repo.txt'
if (-not (Test-Path -LiteralPath $repoFile)) { Write-Host 'repo.txt missing - cannot check for updates.' -ForegroundColor Yellow; exit 1 }
$repo = (Get-Content -LiteralPath $repoFile -Raw).Trim()

function Get-LocalVersion {
    $dll = Join-Path $here 'GhostWatchersTrainer.dll'
    if (-not (Test-Path -LiteralPath $dll)) { return [version]'0.0.0' }
    $v = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion
    if (-not $v) { return [version]'0.0.0' }
    return [version]$v
}

# Files live on the repo's main branch: version.txt + GhostWatchersTrainer.dll
$raw = "https://raw.githubusercontent.com/$repo/main"
$nocache = "?t=$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
Write-Host "Checking github.com/$repo for updates..."
try {
    $remoteText = (Invoke-WebRequest "$raw/version.txt$nocache" -UseBasicParsing -TimeoutSec 20 -Headers @{ 'User-Agent' = 'GhostWatchersTrainer-Updater' }).Content
    if ($remoteText -is [byte[]]) { $remoteText = [Text.Encoding]::UTF8.GetString($remoteText) }
} catch {
    Write-Host "Could not reach GitHub: $($_.Exception.Message)" -ForegroundColor Yellow
    exit 1
}

$remote = [version]($remoteText.Trim().TrimStart('v', 'V'))
$local = Get-LocalVersion
Write-Host "Installed: $local   Newest: $remote"

if ($remote -gt $local) {
    $tmp = Join-Path $env:TEMP 'GhostWatchersTrainer.dll.download'
    Invoke-WebRequest "$raw/GhostWatchersTrainer.dll$nocache" -OutFile $tmp -UseBasicParsing -Headers @{ 'User-Agent' = 'GhostWatchersTrainer-Updater' }
    if ((Get-Item -LiteralPath $tmp).Length -lt 4096) { Write-Host 'Download looks broken, keeping the current version.' -ForegroundColor Yellow; exit 1 }
    Unblock-File -LiteralPath $tmp -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath $tmp (Join-Path $here 'GhostWatchersTrainer.dll') -Force
    Remove-Item -LiteralPath $tmp -Force
    Write-Host "Downloaded version $remote." -ForegroundColor Green
} else {
    Write-Host 'Already the newest version.' -ForegroundColor Green
}

if ($DownloadOnly) { exit 0 }

# Install into the game (closes + restarts it)
$installArgs = @{}
if ($GamePath) { $installArgs.GamePath = $GamePath }
& (Join-Path $here 'Install.ps1') @installArgs -SkipUpdateCheck
