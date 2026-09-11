<#
.SYNOPSIS
  Phase 1 RLS gate: User A cannot PATCH User B's profile.

.DESCRIPTION
  Requires env vars (or parameters):
    SUPABASE_URL, SUPABASE_ANON_KEY
    SOCIAL_A_EMAIL, SOCIAL_A_PASSWORD
    SOCIAL_B_EMAIL, SOCIAL_B_PASSWORD

  Signs in as A and B, verifies A can SELECT B, and that A's PATCH of B
  returns empty representation / denial (not a successful bio change).

.EXAMPLE
  $env:SUPABASE_URL="https://xxx.supabase.co"
  $env:SUPABASE_ANON_KEY="eyJ..."
  $env:SOCIAL_A_EMAIL="a@example.com"; $env:SOCIAL_A_PASSWORD="..."
  $env:SOCIAL_B_EMAIL="b@example.com"; $env:SOCIAL_B_PASSWORD="..."
  .\scripts\verify-phase1-rls.ps1
#>
[CmdletBinding()]
param(
    [string]$Url = $env:SUPABASE_URL,
    [string]$AnonKey = $env:SUPABASE_ANON_KEY,
    [string]$EmailA = $env:SOCIAL_A_EMAIL,
    [string]$PasswordA = $env:SOCIAL_A_PASSWORD,
    [string]$EmailB = $env:SOCIAL_B_EMAIL,
    [string]$PasswordB = $env:SOCIAL_B_PASSWORD
)

$ErrorActionPreference = "Stop"

function Require([string]$name, [string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Missing required value: $name"
    }
}

function Sign-In([string]$baseUrl, [string]$anon, [string]$email, [string]$password) {
    $body = @{ email = $email; password = $password } | ConvertTo-Json
    $resp = Invoke-RestMethod -Method Post -Uri "$baseUrl/auth/v1/token?grant_type=password" `
        -Headers @{ apikey = $anon; Authorization = "Bearer $anon"; "Content-Type" = "application/json" } `
        -Body $body
    return @{
        AccessToken = $resp.access_token
        UserId = $resp.user.id
        Email = $resp.user.email
    }
}

Require "SUPABASE_URL" $Url
Require "SUPABASE_ANON_KEY" $AnonKey
Require "SOCIAL_A_EMAIL" $EmailA
Require "SOCIAL_A_PASSWORD" $PasswordA
Require "SOCIAL_B_EMAIL" $EmailB
Require "SOCIAL_B_PASSWORD" $PasswordB

$base = $Url.TrimEnd("/")
Write-Host "Signing in A ($EmailA) and B ($EmailB)..."
$a = Sign-In $base $AnonKey $EmailA $PasswordA
$b = Sign-In $base $AnonKey $EmailB $PasswordB
Write-Host "A=$($a.UserId)"
Write-Host "B=$($b.UserId)"

$selectHeaders = @{
    apikey = $AnonKey
    Authorization = "Bearer $($a.AccessToken)"
}
$select = Invoke-RestMethod -Method Get `
    -Uri "$base/rest/v1/profiles?id=eq.$($b.UserId)&select=id,display_name,bio" `
    -Headers $selectHeaders
if (-not $select -or $select.Count -lt 1) {
    throw "FAIL: Authenticated A could not SELECT B's profile."
}
Write-Host "PASS: A can SELECT B ($($select[0].display_name))"

$probeBio = "rls-probe-$(Get-Date -Format 'yyyyMMddHHmmss')"
$patchHeaders = @{
    apikey = $AnonKey
    Authorization = "Bearer $($a.AccessToken)"
    "Content-Type" = "application/json"
    Prefer = "return=representation"
}
try {
    $raw = Invoke-WebRequest -Method Patch `
        -Uri "$base/rest/v1/profiles?id=eq.$($b.UserId)" `
        -Headers $patchHeaders `
        -Body (@{ bio = $probeBio } | ConvertTo-Json) `
        -UseBasicParsing
    $status = [int]$raw.StatusCode
    $bodyText = $raw.Content
} catch {
    $status = [int]$_.Exception.Response.StatusCode
    $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
    $bodyText = $reader.ReadToEnd()
}

Write-Host "PATCH other status=$status body=$bodyText"
$mutated = $bodyText -like "*$probeBio*"
if ($mutated) {
    throw "FAIL: A was able to mutate B's bio via REST (RLS broken)."
}
if ($status -ge 200 -and $status -lt 300 -and ($bodyText.Trim() -eq "[]" -or [string]::IsNullOrWhiteSpace($bodyText))) {
    Write-Host "PASS: PATCH returned empty representation (RLS blocked write)."
} elseif ($status -eq 401 -or $status -eq 403) {
    Write-Host "PASS: PATCH denied with HTTP $status."
} else {
    Write-Host "PASS: PATCH did not apply probe bio (status=$status)."
}

# Confirm B's bio unchanged via B's token
$bProfile = Invoke-RestMethod -Method Get `
    -Uri "$base/rest/v1/profiles?id=eq.$($b.UserId)&select=bio" `
    -Headers @{ apikey = $AnonKey; Authorization = "Bearer $($b.AccessToken)" }
if ($bProfile[0].bio -eq $probeBio) {
    throw "FAIL: B's bio equals probe string after A's PATCH."
}
Write-Host "PASS: B's bio unchanged."
Write-Host "Phase 1 RLS gate: ALL CHECKS PASSED"
