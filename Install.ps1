<#
  Installs BepInEx + Ghost Watchers Trainer, then restarts the game.
  In game:  press F9 to open/close the cheat menu
  Uninstall: .\Install.ps1 -Uninstall
#>
param([string]$GamePath, [switch]$Uninstall, [switch]$SkipUpdateCheck)
$ErrorActionPreference = 'Stop'
$AppId = '1850740'
$here = $PSScriptRoot

# Grab the newest version from GitHub first (falls back to the bundled DLL if offline)
if (-not $Uninstall -and -not $SkipUpdateCheck -and (Test-Path -LiteralPath (Join-Path $here 'Update.ps1'))) {
    try { & (Join-Path $here 'Update.ps1') -DownloadOnly } catch { Write-Host "Update check skipped: $($_.Exception.Message)" -ForegroundColor Yellow }
}

function Find-GamePath {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { throw 'Steam not found. Start with -GamePath "<Ghost Watchers folder>".' }
    $libs = @($steam.Replace('/', '\'))
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path $vdf) {
        $libs += [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"') | ForEach-Object { $_.Groups[1].Value.Replace('\\', '\') }
    }
    foreach ($lib in ($libs | Select-Object -Unique)) {
        $acf = Join-Path $lib "steamapps\appmanifest_$AppId.acf"
        if (Test-Path $acf) {
            $dir = [regex]::Match((Get-Content $acf -Raw), '"installdir"\s+"([^"]+)"').Groups[1].Value
            return Join-Path $lib "steamapps\common\$dir"
        }
    }
    throw 'Ghost Watchers is not installed in any Steam library. Start with -GamePath "<Ghost Watchers folder>".'
}

if (-not $GamePath) { $GamePath = Find-GamePath }
Write-Host "Game folder: $GamePath"

$proc = Get-Process 'Ghost Watchers' -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host 'Closing Ghost Watchers...'
    $proc | ForEach-Object { $_.CloseMainWindow() | Out-Null }
    Start-Sleep -Seconds 10
    Get-Process 'Ghost Watchers' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
}

if ($Uninstall) {
    # Only the loader entry point + our plugin; BepInEx folder is left for logs/other mods
    foreach ($f in 'winhttp.dll', 'doorstop_config.ini', 'BepInEx\plugins\GhostWatchersTrainer.dll', 'BepInEx\plugins\GhostWatchersTrainer.dll.new', 'BepInEx\patchers\GhostWatchersUpdater.dll') {
        $p = Join-Path $GamePath $f
        if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force }
    }
    Write-Host 'Trainer removed.' -ForegroundColor Green
} else {
    # Check the package is complete before touching the game (broken unzip = flat files)
    $required = 'bepinex\winhttp.dll', 'bepinex\doorstop_config.ini', 'bepinex\BepInEx\core\BepInEx.dll',
                'bepinex\BepInEx\core\BepInEx.Preloader.dll', 'GhostWatchersTrainer.dll'
    $missing = $required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $here $_)) }
    if ($missing) {
        Write-Host 'ERROR: files are missing from this folder:' -ForegroundColor Red
        $missing | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        Write-Host 'Unzip the zip again with Windows Explorer (right-click -> Extract All) and retry.' -ForegroundColor Yellow
        Write-Host 'If only winhttp.dll is missing, your antivirus deleted it: allow it and retry.' -ForegroundColor Yellow
        exit 1
    }

    Get-ChildItem -LiteralPath $here -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $here 'bepinex\*') $GamePath -Recurse -Force
    New-Item -ItemType Directory -Force (Join-Path $GamePath 'BepInEx\plugins') | Out-Null
    Copy-Item (Join-Path $here 'GhostWatchersTrainer.dll') (Join-Path $GamePath 'BepInEx\plugins') -Force
    $patcher = Join-Path $here 'GhostWatchersUpdater.dll'
    if (Test-Path -LiteralPath $patcher) {
        New-Item -ItemType Directory -Force (Join-Path $GamePath 'BepInEx\patchers') | Out-Null
        Copy-Item -LiteralPath $patcher (Join-Path $GamePath 'BepInEx\patchers') -Force
    }
    $repoTxt = Join-Path $here 'repo.txt'
    if (Test-Path -LiteralPath $repoTxt) { Copy-Item -LiteralPath $repoTxt (Join-Path $GamePath 'BepInEx\plugins\repo.txt') -Force }
    Write-Host 'Trainer installed.' -ForegroundColor Green
}

Write-Host 'Starting Ghost Watchers...'
$started = Get-Date
Start-Process "steam://rungameid/$AppId"
if ($Uninstall) { return }

# Wait for the menu to report in, so problems show up here instead of silently in game
$log = Join-Path $GamePath 'BepInEx\LogOutput.log'
Write-Host 'Waiting for the game to load the cheat menu (up to 2 minutes)...'
$ok = $false
for ($i = 0; $i -lt 60 -and -not $ok; $i++) {
    Start-Sleep -Seconds 2
    if ((Test-Path -LiteralPath $log) -and (Get-Item -LiteralPath $log).LastWriteTime -ge $started) {
        $ok = [bool](Select-String -LiteralPath $log -Pattern 'Cheat menu running' -Quiet)
    }
}

Write-Host ''
if ($ok) {
    Write-Host 'OK: cheat menu is running. Press F9 in game to open it.' -ForegroundColor Green
} else {
    Write-Host 'PROBLEM: the cheat menu did not start.' -ForegroundColor Red
    Write-Host "  Game folder:  $GamePath"
    foreach ($f in 'winhttp.dll', 'doorstop_config.ini', 'BepInEx\core\BepInEx.dll', 'BepInEx\plugins\GhostWatchersTrainer.dll', 'BepInEx\patchers\GhostWatchersUpdater.dll') {
        $exists = Test-Path -LiteralPath (Join-Path $GamePath $f)
        Write-Host ("  {0,-45} {1}" -f $f, $(if ($exists) { 'found' } else { 'MISSING' })) -ForegroundColor $(if ($exists) { 'Gray' } else { 'Red' })
    }
    if (-not (Get-Process 'Ghost Watchers' -ErrorAction SilentlyContinue)) {
        Write-Host '  The game is not running - start it from Steam, then run this installer again.' -ForegroundColor Yellow
    }
    if (Test-Path -LiteralPath $log) {
        Write-Host '  Last lines of BepInEx\LogOutput.log:'
        Get-Content -LiteralPath $log -Tail 8 | ForEach-Object { Write-Host "    $_" }
    } else {
        Write-Host '  No BepInEx log: the mod loader never started (antivirus removed winhttp.dll?).' -ForegroundColor Yellow
    }
    Write-Host 'Send a screenshot of this window to whoever gave you the menu.' -ForegroundColor Yellow
}
