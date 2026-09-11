<#
.SYNOPSIS
  Phase 2 forum RLS gate: A can read B's thread; A cannot UPDATE/DELETE B's thread/post.

.EXAMPLE
  $env:SUPABASE_URL=...; $env:SUPABASE_ANON_KEY=...
  $env:SOCIAL_A_EMAIL=...; $env:SOCIAL_A_PASSWORD=...
  $env:SOCIAL_B_EMAIL=...; $env:SOCIAL_B_PASSWORD=...
  .\scripts\verify-phase2-forum-rls.ps1
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
    if ([string]::IsNullOrWhiteSpace($value)) { throw "Missing required value: $name" }
}

function Sign-In([string]$baseUrl, [string]$anon, [string]$email, [string]$password) {
    $body = @{ email = $email; password = $password } | ConvertTo-Json
    $resp = Invoke-RestMethod -Method Post -Uri "$baseUrl/auth/v1/token?grant_type=password" `
        -Headers @{ apikey = $anon; Authorization = "Bearer $anon"; "Content-Type" = "application/json" } `
        -Body $body
    return @{ AccessToken = $resp.access_token; UserId = $resp.user.id; Email = $resp.user.email }
}

function Invoke-Json([string]$method, [string]$uri, [hashtable]$headers, [string]$body = $null) {
    $params = @{ Method = $method; Uri = $uri; Headers = $headers; UseBasicParsing = $true }
    if ($body) {
        $params["Body"] = $body
        $headers["Content-Type"] = "application/json"
    }
    try {
        $raw = Invoke-WebRequest @params
        return @{ Status = [int]$raw.StatusCode; Body = $raw.Content }
    } catch {
        $status = [int]$_.Exception.Response.StatusCode
        $reader = [System.IO.StreamReader]::new($_.Exception.Response.GetResponseStream())
        return @{ Status = $status; Body = $reader.ReadToEnd() }
    }
}

Require "SUPABASE_URL" $Url
Require "SUPABASE_ANON_KEY" $AnonKey
Require "SOCIAL_A_EMAIL" $EmailA
Require "SOCIAL_A_PASSWORD" $PasswordA
Require "SOCIAL_B_EMAIL" $EmailB
Require "SOCIAL_B_PASSWORD" $PasswordB

$base = $Url.TrimEnd("/")
$general = "11111111-1111-4111-8111-111111111104"

Write-Host "Signing in A ($EmailA) and B ($EmailB)..."
$a = Sign-In $base $AnonKey $EmailA $PasswordA
$b = Sign-In $base $AnonKey $EmailB $PasswordB
Write-Host "A=$($a.UserId)"
Write-Host "B=$($b.UserId)"

$hdrA = @{ apikey = $AnonKey; Authorization = "Bearer $($a.AccessToken)"; Prefer = "return=representation" }
$hdrB = @{ apikey = $AnonKey; Authorization = "Bearer $($b.AccessToken)"; Prefer = "return=representation" }

# Categories readable
$cats = Invoke-RestMethod -Method Get -Uri "$base/rest/v1/forum_categories?select=id,slug&order=sort_order" `
    -Headers @{ apikey = $AnonKey; Authorization = "Bearer $($a.AccessToken)" }
if ($cats.Count -lt 4) { throw "FAIL: Expected >=4 categories, got $($cats.Count)" }
Write-Host "PASS: A can SELECT categories ($($cats.Count))"

# B creates a thread
$stamp = Get-Date -Format "yyyyMMddHHmmss"
$createBody = @{
    category_id = $general
    author_id = $b.UserId
    title = "RLS probe thread $stamp"
    body = "Created by B for Phase 2 RLS gate."
} | ConvertTo-Json
$created = Invoke-Json "POST" "$base/rest/v1/forum_threads" $hdrB $createBody
if ($created.Status -lt 200 -or $created.Status -ge 300) {
    throw "FAIL: B could not create thread ($($created.Status)): $($created.Body)"
}
$thread = ($created.Body | ConvertFrom-Json)[0]
$threadId = $thread.id
Write-Host "PASS: B created thread $threadId"

# A can SELECT B's thread
$sel = Invoke-RestMethod -Method Get -Uri "$base/rest/v1/forum_threads?id=eq.$threadId&select=id,title,author_id" `
    -Headers @{ apikey = $AnonKey; Authorization = "Bearer $($a.AccessToken)" }
if (-not $sel -or $sel.Count -lt 1) { throw "FAIL: A could not SELECT B's thread." }
Write-Host "PASS: A can SELECT B's thread"

# A cannot PATCH B's thread
$probe = "hacked-by-A-$stamp"
$patch = Invoke-Json "PATCH" "$base/rest/v1/forum_threads?id=eq.$threadId" $hdrA (@{ title = $probe } | ConvertTo-Json)
Write-Host "PATCH other thread status=$($patch.Status) body=$($patch.Body)"
if ($patch.Body -like "*$probe*") { throw "FAIL: A mutated B's thread title (RLS broken)." }
Write-Host "PASS: A cannot UPDATE B's thread"

# A cannot DELETE B's thread
$del = Invoke-Json "DELETE" "$base/rest/v1/forum_threads?id=eq.$threadId" $hdrA
$still = Invoke-RestMethod -Method Get -Uri "$base/rest/v1/forum_threads?id=eq.$threadId&select=id" `
    -Headers @{ apikey = $AnonKey; Authorization = "Bearer $($b.AccessToken)" }
if (-not $still -or $still.Count -lt 1) { throw "FAIL: A's DELETE removed B's thread." }
Write-Host "PASS: A cannot DELETE B's thread (status=$($del.Status))"

# B creates a post; A cannot patch it
$postBody = @{
    thread_id = $threadId
    author_id = $b.UserId
    body = "Reply from B $stamp"
} | ConvertTo-Json
$postCreated = Invoke-Json "POST" "$base/rest/v1/forum_posts" $hdrB $postBody
$post = ($postCreated.Body | ConvertFrom-Json)[0]
$postId = $post.id
$postPatch = Invoke-Json "PATCH" "$base/rest/v1/forum_posts?id=eq.$postId" $hdrA (@{ body = $probe } | ConvertTo-Json)
if ($postPatch.Body -like "*$probe*") { throw "FAIL: A mutated B's post (RLS broken)." }
Write-Host "PASS: A cannot UPDATE B's post"

# Cross-read: A creates reply on B's thread (allowed — own author_id)
$replyBody = @{
    thread_id = $threadId
    author_id = $a.UserId
    body = "Cross-platform reply from A $stamp"
} | ConvertTo-Json
$reply = Invoke-Json "POST" "$base/rest/v1/forum_posts" $hdrA $replyBody
if ($reply.Status -lt 200 -or $reply.Status -ge 300) {
    throw "FAIL: A could not reply on B's thread ($($reply.Status)): $($reply.Body)"
}
Write-Host "PASS: A can INSERT own reply on B's thread"

# Like: A likes B's thread
$likeBody = @{ user_id = $a.UserId; thread_id = $threadId } | ConvertTo-Json
$like = Invoke-Json "POST" "$base/rest/v1/forum_likes" $hdrA $likeBody
if ($like.Status -lt 200 -or $like.Status -ge 300) {
    throw "FAIL: A could not like thread ($($like.Status)): $($like.Body)"
}
Write-Host "PASS: A can INSERT own like"

# Local category filter probe: B posts with region_tag AZ; A selects with filter
$localCat = "11111111-1111-4111-8111-111111111103"
$localBody = @{
    category_id = $localCat
    author_id = $b.UserId
    title = "Local AZ probe $stamp"
    body = "Region-tagged"
    region_tag = "AZ"
} | ConvertTo-Json
$localThread = Invoke-Json "POST" "$base/rest/v1/forum_threads" $hdrB $localBody
if ($localThread.Status -lt 200 -or $localThread.Status -ge 300) {
    throw "FAIL: Local thread create failed ($($localThread.Status)): $($localThread.Body)"
}
$az = Invoke-RestMethod -Method Get `
    -Uri "$base/rest/v1/forum_threads?category_id=eq.$localCat&region_tag=eq.AZ&select=id,region_tag" `
    -Headers @{ apikey = $AnonKey; Authorization = "Bearer $($a.AccessToken)" }
$ca = Invoke-RestMethod -Method Get `
    -Uri "$base/rest/v1/forum_threads?category_id=eq.$localCat&region_tag=eq.CA&select=id" `
    -Headers @{ apikey = $AnonKey; Authorization = "Bearer $($a.AccessToken)" }
if ($az.Count -lt 1) { throw "FAIL: region_tag=AZ filter returned no rows." }
if ($ca.Count -gt 0 -and ($ca | Where-Object { $_.id -eq (($localThread.Body | ConvertFrom-Json)[0].id) })) {
    throw "FAIL: CA filter incorrectly included AZ thread."
}
Write-Host "PASS: Local region_tag filter works (AZ visible, CA filter excludes AZ thread)"

Write-Host "Phase 2 forum RLS gate: ALL CHECKS PASSED"
Write-Host "CROSS_SYNC_THREAD_ID=$threadId"
