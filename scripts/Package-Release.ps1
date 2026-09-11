# Package Unbound Rockhound for Windows x64 (portable zip + optional install sync + setup wizard)

param(
    [string]$Configuration = "Release",
    [string]$Version = "0.6.0",
    [string[]]$InstallDirs = @("F:\UnboundRockhound", "G:\UnboundRockhound"),
    [switch]$SkipInstallSync,
    [switch]$BuildSetup
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$AppExe = "UnboundRockhound.exe"
$ProductName = "Unbound Rockhound"
Set-Location $Root

function Sync-UnboundInstall {
    param(
        [string]$StageRoot,
        [string]$TargetDir,
        [string]$VersionLabel,
        [string]$SourceRepo
    )

    Write-Host ""
    Write-Host "Syncing install to: $TargetDir" -ForegroundColor Cyan

    $preserve = @('testdata', 'ui-drive-report')
    if (-not (Test-Path $TargetDir)) {
        New-Item -ItemType Directory -Path $TargetDir -Force | Out-Null
    } else {
        foreach ($name in $preserve) {
            $p = Join-Path $TargetDir $name
            if (Test-Path $p) { Write-Host "  Preserving $name\" }
        }
    }

    foreach ($item in Get-ChildItem $StageRoot -Force) {
        if ($preserve -contains $item.Name) { continue }
        $dest = Join-Path $TargetDir $item.Name
        if ($item.Name -eq 'App') {
            if (-not (Test-Path $dest)) { New-Item -ItemType Directory -Path $dest -Force | Out-Null }
            $robo = robocopy $item.FullName $dest /E /XD "UnboundRockhound.exe.WebView2" /R:2 /W:2 /NFL /NDL /NJH /NJS /nc /ns /np
            if ($LASTEXITCODE -ge 8) { throw "Robocopy failed syncing App folder to $TargetDir (exit $LASTEXITCODE)" }
            continue
        }
        if (Test-Path $dest) { Remove-Item $dest -Recurse -Force -ErrorAction Stop }
        Copy-Item -Path $item.FullName -Destination $dest -Recurse -Force
    }

    $marker = Join-Path $TargetDir "SOURCE-REPO.txt"
    $markerText = @(
        "$ProductName - packaged install (run from here)",
        "================================================",
        "This folder is the app you launch. It is synced from the development repo.",
        "",
        "Source code:  $SourceRepo",
        "Synced:       $(Get-Date -Format 'yyyy-MM-dd HH:mm')",
        "Version:      $VersionLabel",
        "",
        "Run $AppExe from the App subfolder or double-click Launch $ProductName.bat."
    ) -join [Environment]::NewLine
    Set-Content -Path $marker -Value $markerText -Encoding UTF8

    $portableNote = Join-Path $TargetDir "COPY-TO-OTHER-PC.txt"
    $portableText = @(
        "$ProductName - portable copy",
        "==============================",
        "Copy this entire folder to another Windows 10/11 PC (64-bit), then run:",
        "",
        "  Launch $ProductName.bat",
        "",
        "No .NET install required. Optional: ffmpeg, yt-dlp for video features.",
        "",
        "Your data on each PC is stored separately under:",
        "  %LocalAppData%/UnboundRockhound/",
        "",
        "Version: $VersionLabel"
    ) -join [Environment]::NewLine
    Set-Content -Path $portableNote -Value $portableText -Encoding UTF8

    Write-Host "  Install updated at $TargetDir" -ForegroundColor Green
}

Write-Host "Restoring and testing..."
dotnet restore GeoMineralTrace.sln
dotnet test GeoMineralTrace.sln --filter "FullyQualifiedName!~GeoMineralTrace.App" --no-restore
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }

Write-Host "Publishing self-contained x64..."
dotnet restore src\GeoMineralTrace.App\GeoMineralTrace.App.csproj -r win-x64
dotnet publish src\GeoMineralTrace.App\GeoMineralTrace.App.csproj -c $Configuration -p:Platform=x64 -p:PublishProfile=win-x64 -p:SelfContained=true -r win-x64
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

$PublishCandidates = @(
    (Join-Path $Root "src\GeoMineralTrace.App\bin\$Configuration\net8.0-windows10.0.19041.0\win-x64\publish"),
    (Join-Path $Root "src\GeoMineralTrace.App\bin\$Configuration\net8.0-windows10.0.26100.0\win-x64\publish"),
    (Join-Path $Root "src\GeoMineralTrace.App\bin\x64\$Configuration\net8.0-windows10.0.19041.0\win-x64\publish"),
    (Join-Path $Root "src\GeoMineralTrace.App\bin\x64\$Configuration\net8.0-windows10.0.26100.0\win-x64\publish"),
    (Join-Path $Root "dist\publish-win-x64")
)

$PublishDir = $PublishCandidates | Where-Object { Test-Path (Join-Path $_ $AppExe) } | Select-Object -First 1
if (-not $PublishDir) {
    throw "Publish output not found (expected $AppExe). Looked in:`n  $($PublishCandidates -join "`n  ")"
}

$exeTime = (Get-Item (Join-Path $PublishDir $AppExe)).LastWriteTime
Write-Host "Using publish dir: $PublishDir (exe $exeTime)"

$Stage = Join-Path $Root "dist\stage-zip\UnboundRockhound-Windows-x64"
if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $Stage "App") -Force | Out-Null

Copy-Item -Path (Join-Path $PublishDir "*") -Destination (Join-Path $Stage "App") -Recurse -Force
# Dedicated product icon for shortcuts / Start Menu (installer also ships this)
$appIcon = Join-Path $Root "src\GeoMineralTrace.App\Assets\AppIcon.ico"
if (Test-Path $appIcon) {
    Copy-Item -Path $appIcon -Destination (Join-Path $Stage "App\AppIcon.ico") -Force
}
Copy-Item -Path (Join-Path $Root "README.md") -Destination $Stage -Force
Copy-Item -Path (Join-Path $Root "docs") -Destination $Stage -Recurse -Force
Copy-Item -Path (Join-Path $Root "knowledge") -Destination $Stage -Recurse -Force
if (Test-Path (Join-Path $Root "updates")) {
    Copy-Item -Path (Join-Path $Root "updates") -Destination $Stage -Recurse -Force
}
if (Test-Path (Join-Path $Root "EULA-DISCLAIMER.md")) {
    Copy-Item -Path (Join-Path $Root "EULA-DISCLAIMER.md") -Destination $Stage -Force
}
if (Test-Path (Join-Path $Root "data\seed")) {
    New-Item -ItemType Directory -Path (Join-Path $Stage "data\seed") -Force | Out-Null
    Copy-Item -Path (Join-Path $Root "data\seed\*") -Destination (Join-Path $Stage "data\seed") -Force
}

@'
Unbound Rockhound — Windows x64
================================
Publisher: Unbound Infotech Corporation
Web:       https://unboundinfotech.com/
Support:   support@unboundinfotech.com

RECOMMENDED INSTALL
  Run UnboundRockhound-Setup-__VERSION__.exe
  • Branded setup wizard with license agreement
  • Installs under Program Files
  • Creates Start Menu + Desktop shortcuts with the Unbound Rockhound product icon
  • Optional: launch the app when setup finishes

PORTABLE / ZIP
  Unzip anywhere and run "Launch Unbound Rockhound.bat"
  No .NET runtime install required (self-contained build).

REQUIREMENTS
  • Windows 10 (1809+) or Windows 11, 64-bit
  • Optional: ffmpeg (keyframes / shadow measure), yt-dlp (YouTube), whisper CLI (ASR)
  • Map tiles need Online Enrichment (Settings) + internet; markers work offline

FIRST LAUNCH
  Local data is created under %LocalAppData%\UnboundRockhound\
  (Legacy GeoMineral Trace data migrates automatically when present.)
  A 14-day evaluation trial starts automatically. After purchase, open
  Activate and enter your UR-XXXX-XXXX-XXXX-XXXX license key
  (emailed after Stripe checkout). Updates appear as an in-app banner.

QUICK START
  1. Activate — paste license key (or use trial)
  2. Analyze — video / images / YouTube
  3. Evidence Board — review clues; Measure shadow on a keyframe
  4. Solar / Shadow — build locus
  5. Hypotheses / Map — fused locations
  6. Rockhounding / Rivers / Glossary — field reference

LEGAL
  Read EULA-DISCLAIMER.md before commercial or field use.
  Verify land status before visiting. Outputs are hypotheses, not definitive locations.

Version: __VERSION__
'@ -replace '__VERSION__', $Version | Set-Content -Path (Join-Path $Stage "INSTALL.txt") -Encoding UTF8

@'
@echo off
title Unbound Rockhound
cd /d "%~dp0App"
if exist "UnboundRockhound.exe" (
  start "" "UnboundRockhound.exe"
) else (
  echo UnboundRockhound.exe not found in App\
  pause
)
'@ | Set-Content -Path (Join-Path $Stage "Launch Unbound Rockhound.bat") -Encoding ASCII

$ZipName = "UnboundRockhound-Windows-x64-v$Version.zip"
$ZipPath = Join-Path $Root "dist\$ZipName"
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
Compress-Archive -Path $Stage -DestinationPath $ZipPath -CompressionLevel Optimal

if (-not $SkipInstallSync -and $InstallDirs -and $InstallDirs.Count -gt 0) {
    $running = Get-Process -Name "UnboundRockhound" -ErrorAction SilentlyContinue
    if (-not $running) {
        $running = Get-Process -Name "GeoMineralTrace.App" -ErrorAction SilentlyContinue
    }
    if ($running) {
        Write-Host "Closing running $ProductName instance(s)..." -ForegroundColor Yellow
        $running | Stop-Process -Force
        Start-Sleep -Seconds 2
    }

    foreach ($dir in $InstallDirs) {
        if ([string]::IsNullOrWhiteSpace($dir)) { continue }
        $drive = Split-Path $dir -Qualifier
        if ($drive -and -not (Test-Path $drive)) {
            Write-Host "Skipping $dir - drive not available." -ForegroundColor Yellow
            continue
        }
        Sync-UnboundInstall -StageRoot $Stage -TargetDir $dir -VersionLabel $Version -SourceRepo $Root
    }

    if (Test-Path "G:\") {
        $gZip = Join-Path "G:\" $ZipName
        Copy-Item -Path $ZipPath -Destination $gZip -Force
        Write-Host "Portable zip copied to: $gZip" -ForegroundColor Green
    }
}

if ($BuildSetup) {
    & (Join-Path $Root "scripts\Build-Installer.ps1") -Version $Version -Configuration $Configuration
}

Write-Host ""
Write-Host "Package ready:" -ForegroundColor Green
Write-Host $ZipPath
Write-Host ("Size: {0:N1} MB" -f ((Get-Item $ZipPath).Length / 1MB))
Write-Host ""
Write-Host "To build the setup wizard: scripts\Build-Installer.ps1" -ForegroundColor Cyan
