@echo off
setlocal
call "%~dp0_common.bat" require-admin
if errorlevel 1 exit /b %errorlevel%

set "SERVICE_EXE=%~1"
if "%SERVICE_EXE%"=="" set "SERVICE_EXE=%~dp0..\PCMonitor.Service.exe"
if not "%~2"=="" set "PORT=%~2"
for %%I in ("%SERVICE_EXE%") do set "SERVICE_EXE=%%~fI"

if not exist "%SERVICE_EXE%" (
    echo Monitoring executable not found: "%SERVICE_EXE%"
    echo Pass its full path as the first argument or place this scripts folder beside PCMonitor.Service.exe.
    exit /b 3
)

sc.exe query "%SERVICE_NAME%" >nul 2>&1
if errorlevel 1 (
    sc.exe create "%SERVICE_NAME%" binPath= "\"%SERVICE_EXE%\"" start= auto DisplayName= "%SERVICE_DISPLAY_NAME%"
    if errorlevel 1 exit /b %errorlevel%
    sc.exe description "%SERVICE_NAME%" "Monitors PC hardware sensors and exposes them to the private local network."
) else (
    echo The %SERVICE_DISPLAY_NAME% service is already registered; keeping the existing registration.
)

call "%~dp0install-firewall-rule.bat" "%PORT%"
if errorlevel 1 exit /b %errorlevel%
call "%~dp0enable-service.bat"
exit /b %errorlevel%
