@echo off
setlocal DisableDelayedExpansion
chcp 65001 >nul
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-Web.ps1" %*
set "install_exit=%errorlevel%"
if not "%install_exit%"=="0" echo 安装未完成，请查看上面的提示。
if /i "%~1"=="-ValidateOnly" exit /b %install_exit%
pause
exit /b %install_exit%
