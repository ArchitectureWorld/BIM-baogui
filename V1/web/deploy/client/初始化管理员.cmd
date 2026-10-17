@echo off
chcp 65001 >nul
echo 首次初始化管理员。手机号、姓名、密码均由你在服务器交互终端直接输入。
echo 已有管理员时将拒绝重复创建。密码输入时不回显。
ssh -t zetta-pc "sudo docker exec -it hifc-web-hifc-1 dotnet Hifc.Web.Server.dll --bootstrap-admin"
pause
