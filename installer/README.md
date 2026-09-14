# Unbound Rockhound installer

## What the user gets

1. **`UnboundRockhound-Setup-X.Y.Z.exe`** — branded Inno Setup wizard
   - Product icon on the Setup exe itself
   - Welcome panel with Unbound Rockhound mark + publisher
   - License agreement (EULA)
   - Start Menu + Desktop shortcuts that point at **`AppIcon.ico`**
   - Uninstall entry with the same icon
2. **Portable zip** — same binaries without the wizard

## Build

```powershell
# Requires Inno Setup 6+ (winget install JRSoftware.InnoSetup)
.\scripts\Package-Release.ps1 -Version 0.6.0 -SkipInstallSync -BuildSetup
# or
.\scripts\Build-Installer.ps1 -Version 0.6.0
```

Output: `dist\UnboundRockhound-Setup-0.6.0.exe`

## Assets

| File | Role |
|------|------|
| `src/.../Assets/AppIcon.ico` | Setup exe icon, shortcut icon, uninstall icon (Gem Core) |
| `installer/assets/AppIcon.ico` | Staged copy of the same ICO |
| `installer/assets/WizardImage.bmp` | Generated side panel (164×314) |
| `installer/assets/WizardSmallImage.bmp` | Generated header mark (55×55) |
| `installer/WELCOME.txt` | Info-before / welcome copy |
| `Prepare-WizardAssets.ps1` | Regenerates BMPs from AppIcon.ico (Windows) |

Product mark source and regen (SVG → PNG/ICO, including a Linux ICO writer): see [branding/README.md](../branding/README.md).

## Code signing (recommended for SmartScreen)

After building Setup.exe, sign with your Authenticode certificate:

```powershell
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a dist\UnboundRockhound-Setup-0.6.0.exe
signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /a dist\stage-zip\...\App\UnboundRockhound.exe
```

Without signing, Windows may show SmartScreen on first run — expected for new publishers.
