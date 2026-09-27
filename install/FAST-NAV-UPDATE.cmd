@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Zeo-Updates.ps1" -Plugin Nav -FastUpdates
if errorlevel 1 (
  echo Setup failed. Read the error above. No game was closed by this setup.
  pause
  exit /b 1
)
echo Start Space Engineers through Pulsar Legacy to download the latest Nav release.
pause
