@echo off
rem Keep these stable deployment values together and aligned with appsettings.json.
set "SERVICE_NAME=PCMonitor"
set "SERVICE_DISPLAY_NAME=LAN PC Monitor"
set "FIREWALL_RULE_NAME=LAN PC Monitor - API"
set "PORT=5005"

if /I "%~1"=="require-admin" (
    net session >nul 2>&1
    if errorlevel 1 (
        echo Administrator privileges are required. Run this script as administrator.
        exit /b 5
    )
)

exit /b 0
