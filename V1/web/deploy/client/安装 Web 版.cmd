@echo off
chcp 65001 >nul
powershell.exe -NoProfile -File "%~dp0Install-Web.ps1"
if errorlevel 1 echo 安装未完成，请查看上面的提示。
pause
