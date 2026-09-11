# Generates premium Inno Setup wizard bitmaps from AppIcon.ico
# Sizes: modern wizard side panel 164x314, small icon 55x55 (classic) + 110x110 for high-DPI small.
param(
    [string]$AssetsDir = (Join-Path (Split-Path -Parent $PSScriptRoot) "installer\assets"),
    [string]$IconPath = (Join-Path (Split-Path -Parent $PSScriptRoot) "src\GeoMineralTrace.App\Assets\AppIcon.ico")
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Path $AssetsDir -Force | Out-Null
Add-Type -AssemblyName System.Drawing

function Save-Bmp([System.Drawing.Bitmap]$Bitmap, [string]$Path) {
    $Bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $Bitmap.Dispose()
}

function Get-BestIconBitmap([string]$Path, [int]$PreferSize) {
    # Prefer extracting a large frame from the multi-resolution ICO.
    $fs = [System.IO.File]::OpenRead($Path)
    try {
        $icon = New-Object System.Drawing.Icon($fs, $PreferSize, $PreferSize)
        return $icon.ToBitmap()
    }
    finally {
        $fs.Dispose()
    }
}

if (-not (Test-Path $IconPath)) {
    throw "App icon not found: $IconPath"
}

# Brand palette — deep slate / mineral teal (matches Unbound field aesthetic)
$cTop = [System.Drawing.Color]::FromArgb(255, 12, 22, 34)
$cMid = [System.Drawing.Color]::FromArgb(255, 18, 42, 58)
$cBot = [System.Drawing.Color]::FromArgb(255, 8, 56, 64)
$cAccent = [System.Drawing.Color]::FromArgb(255, 56, 178, 172)
$cMuted = [System.Drawing.Color]::FromArgb(255, 160, 184, 196)
$cWhite = [System.Drawing.Color]::FromArgb(255, 245, 248, 250)

$logo = Get-BestIconBitmap $IconPath 256

# ---- Large side panel (164 x 314) ----
$large = New-Object System.Drawing.Bitmap 164, 314
$g = [System.Drawing.Graphics]::FromImage($large)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

$rect = [System.Drawing.Rectangle]::new(0, 0, 164, 314)
$grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $rect, $cTop, $cBot, 90.0)
$cb = New-Object System.Drawing.Drawing2D.ColorBlend
$cb.Colors = @($cTop, $cMid, $cBot)
$cb.Positions = @(0.0, 0.45, 1.0)
$grad.InterpolationColors = $cb
$g.FillRectangle($grad, $rect)
$grad.Dispose()

# Accent bar
$accentBrush = New-Object System.Drawing.SolidBrush $cAccent
$g.FillRectangle($accentBrush, 0, 0, 4, 314)

# Soft vignette circle behind logo
$glow = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(40, 56, 178, 172))
$g.FillEllipse($glow, 22, 48, 120, 120)
$glow.Dispose()

# Product mark
$g.DrawImage($logo, 42, 62, 80, 80)

# Typography
$fontBrand = New-Object System.Drawing.Font("Segoe UI Semibold", 12, [System.Drawing.FontStyle]::Bold)
$fontSub = New-Object System.Drawing.Font("Segoe UI", 8)
$fontCo = New-Object System.Drawing.Font("Segoe UI", 7)
$brushWhite = New-Object System.Drawing.SolidBrush $cWhite
$brushMuted = New-Object System.Drawing.SolidBrush $cMuted

$g.DrawString("UNBOUND", $fontBrand, $brushWhite, 14, 188)
$g.DrawString("ROCKHOUND", $fontBrand, $brushWhite, 14, 208)
$g.DrawString("Field research &", $fontSub, $brushMuted, 14, 240)
$g.DrawString("forensic geolocation", $fontSub, $brushMuted, 14, 254)
$g.DrawString("Unbound Infotech", $fontCo, $accentBrush, 14, 286)

$g.Dispose()
$fontBrand.Dispose(); $fontSub.Dispose(); $fontCo.Dispose()
$brushWhite.Dispose(); $brushMuted.Dispose(); $accentBrush.Dispose()
Save-Bmp $large (Join-Path $AssetsDir "WizardImage.bmp")

# ---- Small header icon (55 x 55) ----
$small = New-Object System.Drawing.Bitmap 55, 55
$g2 = [System.Drawing.Graphics]::FromImage($small)
$g2.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$g2.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    [System.Drawing.Rectangle]::new(0, 0, 55, 55), $cMid, $cBot, 90.0)
$g2.FillRectangle($bg, 0, 0, 55, 55)
$bg.Dispose()
$g2.DrawImage($logo, 6, 6, 43, 43)
$g2.Dispose()
Save-Bmp $small (Join-Path $AssetsDir "WizardSmallImage.bmp")

$logo.Dispose()

# Also stage a dedicated setup/shortcut ICO copy for the installer package
Copy-Item -Path $IconPath -Destination (Join-Path $AssetsDir "AppIcon.ico") -Force

Write-Host "Premium wizard assets written to $AssetsDir"
