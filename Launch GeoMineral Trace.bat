@echo off
setlocal
set "INSTALL=F:\GeoMineralTrace"
if exist "%INSTALL%\App\GeoMineralTrace.App.exe" (
    cd /d "%INSTALL%\App"
    start "" "GeoMineralTrace.App.exe"
    exit /b 0
)
echo GeoMineral Trace install not found at %INSTALL%
echo Run:  powershell -File "%~dp0scripts\Package-Release.ps1"
exit /b 1
