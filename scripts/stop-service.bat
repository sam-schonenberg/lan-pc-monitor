@echo off
setlocal
call "%~dp0_common.bat" require-admin
if errorlevel 1 exit /b %errorlevel%

sc.exe query "%SERVICE_NAME%" >nul 2>&1
if errorlevel 1 (
    echo The %SERVICE_DISPLAY_NAME% service is not installed.
    exit /b 2
)

sc.exe query "%SERVICE_NAME%" | findstr /C:"STOPPED" >nul
if not errorlevel 1 (
    echo The %SERVICE_DISPLAY_NAME% service is already stopped.
    exit /b 0
)

sc.exe stop "%SERVICE_NAME%"
if errorlevel 1 exit /b %errorlevel%

for /L %%I in (1,1,30) do (
    sc.exe query "%SERVICE_NAME%" | findstr /C:"STOPPED" >nul && (
        echo The %SERVICE_DISPLAY_NAME% service is stopped.
        exit /b 0
    )
    timeout /t 1 /nobreak >nul
)

echo The service did not stop within 30 seconds.
exit /b 1
