@echo off
setlocal
chcp 65001 >nul
set "MCP_SERVER=%LOCALAPPDATA%\BIMBaoGui\McpServer\0.3.0\BIMBaoGui.McpServer.exe"

echo ========================================
echo BIMBaoGui Revit MCP 连接检测
echo ========================================
echo.

if not exist "%MCP_SERVER%" (
  echo 未找到已安装的 BIMBaoGui.McpServer.exe。
  echo 请先运行 Install.cmd。
  set "BIMBAOGUI_EXIT_CODE=4"
  goto :finish
)

"%MCP_SERVER%" --probe
set "BIMBAOGUI_EXIT_CODE=%ERRORLEVEL%"

:finish
echo.
if "%BIMBAOGUI_EXIT_CODE%"=="0" (
  echo MCP Bridge 已连接。
) else if "%BIMBAOGUI_EXIT_CODE%"=="2" (
  echo 未发现已加载 BIMBaoGui 的 Revit 2020。请启动 Revit 2020 后重试。
) else if "%BIMBAOGUI_EXIT_CODE%"=="3" (
  echo 检测到多个 Revit 会话。调用工具时需要指定 revit_process_id。
) else (
  echo MCP 检测失败，退出码：%BIMBAOGUI_EXIT_CODE%
)
echo.
pause
exit /b %BIMBAOGUI_EXIT_CODE%
