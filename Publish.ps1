<#
  Builds the trainer, bumps the version, packs the friend zip and pushes everything to GitHub
  (git over SSH). Friends' "Update Trainer.bat" and the in-game notice read version.txt +
  GhostWatchersTrainer.dll from the repo's main branch.

  Usage:  .\Publish.ps1                 -> bumps the patch version (2.1.0 -> 2.1.1)
          .\Publish.ps1 -Version 2.2.0  -> explicit version
          .\Publish.ps1 -NoUpload       -> build + zip only
#>
param([string]$Version, [switch]$NoUpload, [string]$Notes = '')
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = $PSScriptRoot
$src = Join-Path $root 'src'
$repo = (Get-Content -LiteralPath (Join-Path $root 'repo.txt') -Raw).Trim()

# ---- version -------------------------------------------------------------------
$versionFile = Join-Path $src 'Version.cs'
$current = [regex]::Match((Get-Content -LiteralPath $versionFile -Raw), 'Current = "([0-9.]+)"').Groups[1].Value
if (-not $Version) {
    $v = [version]$current
    $Version = "{0}.{1}.{2}" -f $v.Major, $v.Minor, ($v.Build + 1)
}
Write-Host "Version: $current -> $Version"
(Get-Content -LiteralPath $versionFile -Raw) -replace 'Current = "[0-9.]+"', "Current = `"$Version`"" | Set-Content -LiteralPath $versionFile -Encoding utf8 -NoNewline
$proj = Join-Path $src 'GhostWatchersTrainer.csproj'
(Get-Content -LiteralPath $proj -Raw) -replace '<Version>[0-9.]+</Version>', "<Version>$Version</Version>" | Set-Content -LiteralPath $proj -Encoding utf8 -NoNewline

# ---- build ---------------------------------------------------------------------
Push-Location $src
try {
    dotnet build -c Release "-p:BepInExDir=$(Join-Path $root 'bepinex')" -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
} finally { Pop-Location }
$dll = Join-Path $src 'bin\Release\net472\GhostWatchersTrainer.dll'
Copy-Item -LiteralPath $dll (Join-Path $root 'GhostWatchersTrainer.dll') -Force

# auto-update patcher (runs before plugins load and swaps in new versions)
Push-Location (Join-Path $src 'Patcher')
try {
    dotnet build -c Release "-p:BepInExDir=$(Join-Path $root 'bepinex')" -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Patcher build failed' }
} finally { Pop-Location }
Copy-Item -LiteralPath (Join-Path $src 'Patcher\bin\Release\net472\GhostWatchersUpdater.dll') (Join-Path $root 'GhostWatchersUpdater.dll') -Force

# ---- zip (forward-slash entry names so every unzip tool keeps the folders) --------------
$stage = Join-Path $env:TEMP "gwt_stage_$([guid]::NewGuid().ToString('N'))"
$pkg = Join-Path $stage 'GhostWatchersCheatMenu'
New-Item -ItemType Directory -Force (Join-Path $pkg 'Save Editor') | Out-Null
foreach ($f in 'Install Trainer.bat', 'Uninstall Trainer.bat', 'Update Trainer.bat', 'Install.ps1', 'Update.ps1', 'repo.txt', 'README.txt', 'GhostWatchersTrainer.dll', 'GhostWatchersUpdater.dll') {
    Copy-Item -LiteralPath (Join-Path $root $f) $pkg
}
Copy-Item -LiteralPath (Join-Path $root 'bepinex') (Join-Path $pkg 'bepinex') -Recurse
$saveEditor = Join-Path (Split-Path $root) 'GhostWatchersSaveEditor'
if (Test-Path -LiteralPath $saveEditor) {
    Copy-Item -LiteralPath (Join-Path $saveEditor 'GhostWatchersSaveEditor.ps1'), (Join-Path $saveEditor 'Run Save Editor.bat') (Join-Path $pkg 'Save Editor')
}

$zip = Join-Path $root 'GhostWatchersCheatMenu.zip'
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$fs = [IO.File]::Open($zip, 'CreateNew')
$za = New-Object IO.Compression.ZipArchive($fs, [IO.Compression.ZipArchiveMode]::Create)
foreach ($f in Get-ChildItem -LiteralPath $stage -Recurse -File -Force) {
    $name = $f.FullName.Substring($stage.Length + 1) -replace '\\', '/'
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $f.FullName, $name) | Out-Null
}
$za.Dispose(); $fs.Dispose()
Remove-Item -LiteralPath $stage -Recurse -Force
Write-Host "Zip: $zip" -ForegroundColor Green

if ($NoUpload) { return }

# ---- push to GitHub (git over SSH) ---------------------------------------------------
Set-Content -LiteralPath (Join-Path $root 'version.txt') $Version -Encoding ascii -NoNewline
$git = (Get-Command git -ErrorAction SilentlyContinue).Source
if (-not $git) { $git = 'C:\Program Files\Git\cmd\git.exe' }
Push-Location $root
$prevEap = $ErrorActionPreference; $ErrorActionPreference = 'Continue'  # git writes progress/warnings to stderr
try {
    & $git add -A
    & $git commit -q -m ("v$Version" + $(if ($Notes) { " - $Notes" } else { '' }))
    & $git push -q origin main
    if ($LASTEXITCODE -ne 0) { throw 'git push failed (is your SSH key added on github.com?)' }
} finally { $ErrorActionPreference = $prevEap; Pop-Location }
Write-Host "Published v$Version to https://github.com/$repo" -ForegroundColor Green