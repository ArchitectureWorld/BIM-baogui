@echo off
chcp 65001 >nul
echo HIFC Web server environment setup
echo Connects to the saved zetta-pc host and runs the reviewed Docker bootstrap.
echo Type the sudo password in this terminal only. Do not send it to Codex.
ssh -tt -o BatchMode=yes zetta-pc "sudo bash /home/zetta/hifc-web-bootstrap-20261017/bootstrap-host.sh --apply"
set result=%ERRORLEVEL%
echo Exit code: %result%
pause
exit /b %result%
