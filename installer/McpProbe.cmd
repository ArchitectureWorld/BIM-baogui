@echo off
setlocal
chcp 65001 >nul
set "MCP_SERVER=%LOCALAPPDATA%\BIMBaoGui\McpServer\0.4.3\BIMBaoGui.McpServer.exe"

if not exist "%MCP_SERVER%" (
  echo 未找到已安装的 BIMBaoGui MCP Server：
  echo %MCP_SERVER%
  pause
  exit /b 4
)

"%MCP_SERVER%" --probe
set "BIMBAOGUI_EXIT_CODE=%ERRORLEVEL%"
echo.
if "%BIMBAOGUI_EXIT_CODE%"=="0" echo MCP Bridge 已连接。
if "%BIMBAOGUI_EXIT_CODE%"=="2" echo 未发现 Revit Bridge。请启动 Revit 2020 并确认插件已加载。
if "%BIMBAOGUI_EXIT_CODE%"=="3" echo 检测到多个 Revit 会话。请在 MCP 工具参数中明确 revit_process_id。
if "%BIMBAOGUI_EXIT_CODE%"=="4" echo MCP 探针发生技术错误。
echo.
pause
exit /b %BIMBAOGUI_EXIT_CODE%
