<#
.SYNOPSIS
  Phase 3: show_reddit_discovery toggle + From Reddit / FB / discoveries SELECT.
#>
[CmdletBinding()]
param(
    [string]$Url = $env:SUPABASE_URL,
    [string]$AnonKey = $env:SUPABASE_ANON_KEY,
    [string]$EmailA = $env:SOCIAL_A_EMAIL,
    [string]$PasswordA = $env:SOCIAL_A_PASSWORD
)

$ErrorActionPreference = "Stop"

function Require([string]$name, [string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { throw "Missing required value: $name" }
}

function Sign-In([string]$baseUrl, [string]$anon, [string]$email, [string]$password) {
    $body = @{ email = $email; password = $password } | ConvertTo-Json
    $resp = Invoke-RestMethod -Method Post -Uri ($baseUrl + '/auth/v1/token?grant_type=password') `
        -Headers @{ apikey = $anon; Authorization = ('Bearer ' + $anon); 'Content-Type' = 'application/json' } `
        -Body $body
    return @{ AccessToken = $resp.access_token; UserId = $resp.user.id }
}

Require 'SUPABASE_URL' $Url
Require 'SUPABASE_ANON_KEY' $AnonKey
Require 'SOCIAL_A_EMAIL' $EmailA
Require 'SOCIAL_A_PASSWORD' $PasswordA

$base = $Url.TrimEnd('/')
$a = Sign-In $base $AnonKey $EmailA $PasswordA
Write-Host ('A=' + $a.UserId)
$authHdr = @{
    apikey = $AnonKey
    Authorization = ('Bearer ' + $a.AccessToken)
}
$writeHdr = @{
    apikey = $AnonKey
    Authorization = ('Bearer ' + $a.AccessToken)
    Prefer = 'return=representation'
    'Content-Type' = 'application/json'
}

$cats = Invoke-RestMethod -Method Get -Uri ($base + '/rest/v1/forum_categories?slug=eq.from-reddit&select=id,slug') -Headers $authHdr
if (-not $cats -or $cats.Count -lt 1) {
    throw 'FAIL: from-reddit category missing - apply phase3 migration'
}
Write-Host 'PASS: from-reddit category present'

$profUri = $base + '/rest/v1/profiles?id=eq.' + $a.UserId + '&select=id,show_reddit_discovery'
$before = Invoke-RestMethod -Method Get -Uri $profUri -Headers $authHdr
Write-Host ('show_reddit_discovery before=' + $before[0].show_reddit_discovery)

$patchUri = $base + '/rest/v1/profiles?id=eq.' + $a.UserId
$patchOff = Invoke-WebRequest -Method Patch -Uri $patchUri -Headers $writeHdr -Body '{"show_reddit_discovery":false}' -UseBasicParsing
$off = ($patchOff.Content | ConvertFrom-Json)[0]
if ($off.show_reddit_discovery -ne $false) { throw 'FAIL: could not set show_reddit_discovery=false' }
Write-Host 'PASS: toggled show_reddit_discovery=false'

$patchOn = Invoke-WebRequest -Method Patch -Uri $patchUri -Headers $writeHdr -Body '{"show_reddit_discovery":true}' -UseBasicParsing
$on = ($patchOn.Content | ConvertFrom-Json)[0]
if ($on.show_reddit_discovery -ne $true) { throw 'FAIL: could not set show_reddit_discovery=true' }
Write-Host 'PASS: toggled show_reddit_discovery=true'

$disc = Invoke-RestMethod -Method Get -Uri ($base + '/rest/v1/reddit_discoveries?select=id&limit=5') -Headers $authHdr
Write-Host ('PASS: reddit_discoveries SELECT count=' + $disc.Count)

$fb = Invoke-RestMethod -Method Get -Uri ($base + '/rest/v1/facebook_directory?select=id,name&order=sort_order') -Headers $authHdr
Write-Host ('PASS: facebook_directory SELECT count=' + $fb.Count)

$mentions = Invoke-RestMethod -Method Get -Uri ($base + '/rest/v1/community_location_mentions?select=id,lat,lon,place_hint&limit=10') -Headers $authHdr
Write-Host ('PASS: community_location_mentions SELECT count=' + $mentions.Count)

try {
    $raw = Invoke-WebRequest -Method Post -Uri ($base + '/rest/v1/reddit_discoveries') -Headers $writeHdr `
        -Body '{"reddit_thing_id":"t3_should_fail","subreddit":"x","title":"nope","url":"https://example.com/x"}' -UseBasicParsing
    $status = [int]$raw.StatusCode
    $body = $raw.Content
} catch {
    $status = [int]$_.Exception.Response.StatusCode
    $body = ''
}
if ($body -like '*t3_should_fail*') { throw 'FAIL: authenticated client wrote reddit_discoveries' }
Write-Host ('PASS: client cannot INSERT reddit_discoveries status=' + $status)

Write-Host 'Phase 3 reddit toggle / directory gate: ALL CHECKS PASSED'
