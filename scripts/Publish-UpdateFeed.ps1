# Write / refresh the public update feed JSON for Unbound Rockhound.
# Upload the resulting file to your HTTPS host (same URL as AppUpdateService.DefaultFeedUrl).

param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$DownloadUrl,

    [string]$Notes = "",
    [string]$Title = "",
    [string]$ReleaseNotesUrl = "https://unboundinfotech.com/products/unbound-rockhound/changelog",
    [switch]$Mandatory,
    [string]$ProductId = "unbound-rockhound",
    [string]$OutFile = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

if (-not $Title) { $Title = "Unbound Rockhound $Version" }
if (-not $OutFile) {
    $OutFile = Join-Path $Root "updates\$ProductId\latest.json"
}

$dir = Split-Path -Parent $OutFile
if (-not (Test-Path $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

$manifest = [ordered]@{
    productId         = $ProductId
    version           = $Version
    releasedAtUtc     = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    title             = $Title
    notes             = $Notes
    downloadUrl       = $DownloadUrl
    releaseNotesUrl   = $ReleaseNotesUrl
    mandatory         = [bool]$Mandatory
}

$json = $manifest | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText($OutFile, $json + "`n", [System.Text.UTF8Encoding]::new($false))

Write-Host "Wrote $OutFile" -ForegroundColor Green
Write-Host $json
Write-Host ""
Write-Host "Next: upload this file to your public feed URL, e.g." -ForegroundColor Cyan
Write-Host "  https://updates.unboundrockhound.app/unbound-rockhound/latest.json"
Write-Host "  or https://unboundinfotech.com/updates/unbound-rockhound/latest.json"
Write-Host "Then point Settings feed URL / DNS at that location."
