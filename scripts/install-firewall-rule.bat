@echo off
setlocal
call "%~dp0_common.bat" require-admin
if errorlevel 1 exit /b %errorlevel%
if not "%~1"=="" set "PORT=%~1"

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-firewall-rule.ps1" -Port %PORT%
exit /b %errorlevel%
