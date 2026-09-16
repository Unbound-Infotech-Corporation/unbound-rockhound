# Build Unbound Rockhound Inno Setup installer (requires Inno Setup 6+).
param(
    [string]$Version = "0.7.0",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $Root

$stageApp = Join-Path $Root "dist\stage-zip\UnboundRockhound-Windows-x64\App"
$publishExe = Join-Path $stageApp "UnboundRockhound.exe"
$legacyExe = Join-Path $stageApp "GeoMineralTrace.App.exe"

if (-not (Test-Path $publishExe) -and -not (Test-Path $legacyExe)) {
    Write-Host "Publish output not staged. Running Package-Release.ps1 first..." -ForegroundColor Yellow
    & (Join-Path $Root "scripts\Package-Release.ps1") -Configuration $Configuration -Version $Version
}

if (-not (Test-Path $publishExe) -and (Test-Path $legacyExe)) {
    throw "Stage folder still has GeoMineralTrace.App.exe - rebuild with AssemblyName UnboundRockhound."
}

$isccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
    (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $isccCandidates) {
    Write-Host "Inno Setup 6+ (ISCC.exe) not found." -ForegroundColor Red
    Write-Host "Install: winget install JRSoftware.InnoSetup" -ForegroundColor Yellow
    Write-Host "Portable zip is still available under dist\UnboundRockhound-Windows-x64-v$Version.zip" -ForegroundColor Cyan
    exit 2
}

$assetsDir = Join-Path $Root "installer\assets"
New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
& (Join-Path $Root "installer\Prepare-WizardAssets.ps1") -AssetsDir $assetsDir

$iss = Join-Path $Root "installer\UnboundRockhound.iss"
Write-Host "Compiling installer with $isccCandidates ..." -ForegroundColor Cyan
& $isccCandidates "/DMyAppVersion=$Version" $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit $LASTEXITCODE" }

$setup = Join-Path $Root "dist\UnboundRockhound-Setup-$Version.exe"
if (Test-Path $setup) {
    Write-Host "Setup ready: $setup" -ForegroundColor Green
    Write-Host ("Size: {0:N1} MB" -f ((Get-Item $setup).Length / 1MB))
}
