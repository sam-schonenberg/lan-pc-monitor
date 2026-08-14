@echo off
setlocal
call "%~dp0_common.bat" require-admin
if errorlevel 1 exit /b %errorlevel%

call "%~dp0stop-service.bat"
if errorlevel 1 exit /b %errorlevel%
call "%~dp0start-service.bat"
exit /b %errorlevel%
