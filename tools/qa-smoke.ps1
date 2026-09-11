# GeoMineral Trace — QA smoke harness (UI + process probe)
# Run: powershell -ExecutionPolicy Bypass -File tools/qa-smoke.ps1

$ErrorActionPreference = "Stop"
$ReportDir = Join-Path $env:LOCALAPPDATA "GeoMineralTrace\qa"
New-Item -ItemType Directory -Force -Path $ReportDir | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$log = Join-Path $ReportDir "smoke-$stamp.log"

function Write-QaLog([string]$msg) {
    $line = "$(Get-Date -Format o)  $msg"
    Add-Content -Path $log -Value $line -Encoding UTF8
    Write-Host $line
}

Write-QaLog "=== QA smoke start ==="
Write-QaLog "Machine: $env:COMPUTERNAME  User: $env:USERNAME"

$exeCandidates = @(
    "C:\Users\akind\Documents\GeoMineralTrace\src\GeoMineralTrace.App\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\GeoMineralTrace.App.exe",
    "C:\Users\akind\Documents\GeoMineralTrace\src\GeoMineralTrace.App\bin\Debug\net8.0-windows10.0.19041.0\win-x64\GeoMineralTrace.App.exe"
)
$exe = $exeCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $exe) { throw "App exe not found. Build x64 Debug first." }
Write-QaLog "Exe: $exe"

$crashBefore = Join-Path $env:LOCALAPPDATA "GeoMineralTrace\crash.log"
$crashSizeBefore = if (Test-Path $crashBefore) { (Get-Item $crashBefore).Length } else { 0 }
Write-QaLog "crash.log size before: $crashSizeBefore"

$dataDir = Join-Path $env:LOCALAPPDATA "GeoMineralTrace"
foreach ($f in @("localities.db","claims.db","finds.db","crash.log")) {
    $p = Join-Path $dataDir $f
    if (Test-Path $p) {
        $item = Get-Item $p
        Write-QaLog ("Data file: {0}  {1} bytes  LW={2}" -f $f, $item.Length, $item.LastWriteTime)
    } else {
        Write-QaLog "Data file missing: $f"
    }
}

$proc = Start-Process -FilePath $exe -PassThru -WorkingDirectory (Split-Path $exe)
Write-QaLog "Launched PID=$($proc.Id)"
Start-Sleep -Seconds 10

if ($proc.HasExited) {
    Write-QaLog "FAIL: App exited early ExitCode=$($proc.ExitCode)"
} else {
    Write-QaLog "App still running after 10s - OK"
}

try {
    $children = Get-CimInstance Win32_Process | Where-Object { $_.ParentProcessId -eq $proc.Id }
    foreach ($c in $children) {
        Write-QaLog ("Child process: Id={0} Name={1}" -f $c.ProcessId, $c.Name)
    }
    $p = Get-Process -Id $proc.Id -ErrorAction Stop
    Write-QaLog ("WorkingSetMB={0}  Threads={1}  Handles={2}" -f ([math]::Round($p.WorkingSet64/1MB,1)), $p.Threads.Count, $p.HandleCount)
    Get-Process | Where-Object { $_.ProcessName -match 'msedgewebview2|GeoMineral|Crashpad' } | ForEach-Object {
        Write-QaLog ("Related process: {0} PID={1} WS_MB={2}" -f $_.ProcessName, $_.Id, ([math]::Round($_.WorkingSet64/1MB,1)))
    }
} catch {
    Write-QaLog "Process probe error: $($_.Exception.Message)"
}

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

function Get-WindowByProcessId([int]$processId) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}

$win = $null
for ($i = 0; $i -lt 20; $i++) {
    $win = Get-WindowByProcessId $proc.Id
    if ($win) { break }
    Start-Sleep -Seconds 1
}

if (-not $win) {
    Write-QaLog "WARN: Could not find main window via UIA"
} else {
    Write-QaLog ("Main window Name='{0}'" -f $win.Current.Name)

    $navNames = @(
        "Home","Analyze","Cases","Evidence Board","Hypotheses","Solar / Shadow",
        "Rockhounding","Claims","Map","Reports","Techniques KB","About","Settings"
    )

    foreach ($nav in $navNames) {
        try {
            $nameCond = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty, $nav)
            $el = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
            if (-not $el) {
                Write-QaLog "NAV MISS: $nav"
                continue
            }
            $handled = $false
            try {
                $inv = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                if ($inv) { $inv.Invoke(); Write-QaLog "NAV OK invoke: $nav"; $handled = $true }
            } catch {}
            if (-not $handled) {
                try {
                    $sel = $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
                    if ($sel) { $sel.Select(); Write-QaLog "NAV OK select: $nav"; $handled = $true }
                } catch {}
            }
            if (-not $handled) {
                Write-QaLog ("NAV NO PATTERN: {0} type={1}" -f $nav, $el.Current.ControlType.ProgrammaticName)
            }
            Start-Sleep -Milliseconds 1500
        } catch {
            Write-QaLog "NAV ERROR ${nav}: $($_.Exception.Message)"
        }
    }

    # Re-open Map then Claims/Rockhounding and click key buttons
    foreach ($nav in @("Rockhounding","Claims","Map")) {
        $nameCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $nav)
        $el = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
        if ($el) {
            try {
                $sel = $el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
                if ($sel) { $sel.Select() } else {
                    $inv = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                    if ($inv) { $inv.Invoke() }
                }
            } catch {}
            Start-Sleep -Seconds 2
        }

        $buttons = switch ($nav) {
            "Rockhounding" { @("Search","Open in Google Earth") }
            "Claims" { @("Search","Open in Google Earth") }
            "Map" { @("Export All Visible","Refresh from session") }
            default { @() }
        }
        foreach ($btn in $buttons) {
            try {
                $btnCond = New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, $btn)
                $btnEl = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
                if (-not $btnEl) {
                    Write-QaLog "BTN miss on ${nav}: $btn"
                    continue
                }
                $inv = $btnEl.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                if ($inv) {
                    $inv.Invoke()
                    Write-QaLog "BTN OK on ${nav}: $btn"
                    Start-Sleep -Seconds 2
                    # Dismiss any ContentDialog (Escape)
                    [System.Windows.Forms.SendKeys]::SendWait("{ESC}")
                    Start-Sleep -Milliseconds 400
                }
            } catch {
                Write-QaLog "BTN ERROR ${btn}: $($_.Exception.Message)"
            }
        }
    }
}

Start-Sleep -Seconds 2
$crashSizeAfter = if (Test-Path $crashBefore) { (Get-Item $crashBefore).Length } else { 0 }
Write-QaLog "crash.log size after: $crashSizeAfter"
if ($crashSizeAfter -gt $crashSizeBefore) {
    Write-QaLog "NEW CRASH LOG CONTENT:"
    Get-Content $crashBefore -Tail 50 | ForEach-Object { Write-QaLog "  $_" }
}

if (-not $proc.HasExited) {
    Write-QaLog "Closing app..."
    try { [void]$proc.CloseMainWindow(); Start-Sleep -Seconds 4 } catch {}
    if (-not $proc.HasExited) {
        Write-QaLog "Force stop PID=$($proc.Id)"
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        # also kill leftover webview children if orphaned
        Get-Process -Name "msedgewebview2" -ErrorAction SilentlyContinue | Where-Object {
            try { $_.MainModule.FileName -like "*GeoMineral*" } catch { $false }
        } | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}

Write-QaLog "=== QA smoke end ==="
Write-Host "LOG=$log"
