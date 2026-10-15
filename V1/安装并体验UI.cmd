@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\BuildAndInstall-GlassUI.ps1"
if errorlevel 1 (
  echo.
  echo H-IFC UI build/install failed. Review the message above.
) else (
  echo.
  echo H-IFC UI build/install finished. Open Revit 2020 to test.
)
echo.
pause
