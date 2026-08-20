@echo off
setlocal
chcp 65001 >nul
set "PACKAGE_ROOT=%~dp0"
set "REVIT_PAYLOAD=%PACKAGE_ROOT%BIMBaoGui.RevitAddin"
set "MCP_PAYLOAD=%PACKAGE_ROOT%BIMBaoGui.McpServer"

echo ========================================
echo BIMBaoGui Revit 2020 + MCP 安装程序
echo ========================================
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PACKAGE_ROOT%Install-Revit2020.ps1" -SourceRoot "%PACKAGE_ROOT%."
set "BIMBAOGUI_EXIT_CODE=%ERRORLEVEL%"

echo.
if not "%BIMBAOGUI_EXIT_CODE%"=="0" (
  echo 安装失败。请保留本窗口中的错误信息。
) else (
  echo 安装完成。现在可以启动 Revit 2020，并配置 MCP Client。
  echo 可双击 McpProbe.cmd 检查 Revit Bridge 连接。
)
echo.
pause
exit /b %BIMBAOGUI_EXIT_CODE%
