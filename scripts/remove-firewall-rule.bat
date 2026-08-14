@echo off
setlocal
call "%~dp0_common.bat" require-admin
if errorlevel 1 exit /b %errorlevel%

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0remove-firewall-rule.ps1"
exit /b %errorlevel%
