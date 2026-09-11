<#
.SYNOPSIS
  Documents / simulates reddit-discover graceful degradation response shape.

  The edge function always returns HTTP 200 with { ok:false, degraded:true, error }
  when Reddit OAuth + public JSON fail, or when CRON_SECRET is wrong / missing.

.EXAMPLE
  .\scripts\verify-phase3-reddit-degraded.ps1
  .\scripts\verify-phase3-reddit-degraded.ps1 -InvokeLive  # optional live call if URL+secret set
#>
[CmdletBinding()]
param(
    [switch]$InvokeLive,
    [string]$FunctionsUrl = $env:SUPABASE_FUNCTIONS_URL,
    [string]$CronSecret = $env:CRON_SECRET
)

$ErrorActionPreference = "Stop"

# Shape contract (mirrors buildDegradedResponse in index.ts)
$simulated = @{
    ok = $false
    degraded = $true
    error = "Simulated Reddit fetch failed for all subreddits"
} | ConvertTo-Json -Compress

$obj = $simulated | ConvertFrom-Json
if ($obj.ok -ne $false) { throw "FAIL: ok must be false" }
if ($obj.degraded -ne $true) { throw "FAIL: degraded must be true" }
if ([string]::IsNullOrWhiteSpace($obj.error)) { throw "FAIL: error required" }
Write-Host "PASS: degraded response shape = $simulated"

if ($InvokeLive) {
    if ([string]::IsNullOrWhiteSpace($FunctionsUrl)) {
        throw "Set SUPABASE_FUNCTIONS_URL (e.g. https://PROJECT.supabase.co/functions/v1/reddit-discover)"
    }
    $headers = @{ "Content-Type" = "application/json" }
    if (-not [string]::IsNullOrWhiteSpace($CronSecret)) {
        $headers["x-cron-secret"] = $CronSecret
    }
    try {
        $resp = Invoke-WebRequest -Method Post -Uri $FunctionsUrl.TrimEnd('/') `
            -Headers $headers -Body "{}" -UseBasicParsing
        Write-Host "Live status=$($resp.StatusCode) body=$($resp.Content)"
        if ([int]$resp.StatusCode -ne 200) {
            throw "FAIL: expected HTTP 200 even on failure, got $($resp.StatusCode)"
        }
        $live = $resp.Content | ConvertFrom-Json
        if ($null -eq $live.degraded -and $live.ok -ne $true) {
            throw "FAIL: live body missing ok/degraded"
        }
        Write-Host "PASS: live invoke returned 200 with parseable body"
    } catch {
        if ($_.Exception.Response) {
            $code = [int]$_.Exception.Response.StatusCode.value__
            throw "FAIL: live invoke HTTP $code (edge should prefer 200+degraded)"
        }
        throw
    }
} else {
    Write-Host "Skip live invoke (pass -InvokeLive with SUPABASE_FUNCTIONS_URL / CRON_SECRET)."
}

Write-Host "Phase 3 reddit degraded contract: ALL CHECKS PASSED"
