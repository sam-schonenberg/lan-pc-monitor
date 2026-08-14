@echo off
setlocal
call "%~dp0_common.bat" require-admin
if errorlevel 1 exit /b %errorlevel%

sc.exe query "%SERVICE_NAME%" >nul 2>&1
if not errorlevel 1 (
    call "%~dp0stop-service.bat"
    if errorlevel 1 exit /b %errorlevel%
    sc.exe delete "%SERVICE_NAME%"
    if errorlevel 1 exit /b %errorlevel%
    echo The %SERVICE_DISPLAY_NAME% service registration was removed.
) else (
    echo The %SERVICE_DISPLAY_NAME% service is not installed.
)

call "%~dp0remove-firewall-rule.bat"
exit /b %errorlevel%
