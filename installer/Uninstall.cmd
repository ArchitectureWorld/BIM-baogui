@echo off
setlocal
chcp 65001 >nul
set "PACKAGE_ROOT=%~dp0"

echo ========================================
echo BIMBaoGui Revit 2020 原生插件卸载程序
echo ========================================
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PACKAGE_ROOT%Install-Revit2020.ps1" -Uninstall
set "BIMBAOGUI_EXIT_CODE=%ERRORLEVEL%"

echo.
if not "%BIMBAOGUI_EXIT_CODE%"=="0" (
  echo 卸载失败。请保留本窗口中的错误信息。
) else (
  echo 卸载完成。
)
echo.
pause
exit /b %BIMBAOGUI_EXIT_CODE%
