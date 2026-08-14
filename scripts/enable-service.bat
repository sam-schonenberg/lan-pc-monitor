@echo off
setlocal
call "%~dp0_common.bat" require-admin
if errorlevel 1 exit /b %errorlevel%

sc.exe query "%SERVICE_NAME%" >nul 2>&1
if errorlevel 1 (
    echo The %SERVICE_DISPLAY_NAME% service is not installed.
    exit /b 2
)

sc.exe config "%SERVICE_NAME%" start= auto
if errorlevel 1 exit /b %errorlevel%
call "%~dp0start-service.bat"
exit /b %errorlevel%
