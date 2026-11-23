@echo off
REM ============================================================
REM  Network Watchdog - Full Uninstall (standalone)
REM  Removes: scheduled task, registry startup entry,
REM           settings/log folders, and the EXE file.
REM
REM  Run as Administrator (right-click > Run as administrator)
REM  Compatible with Windows 7 SP1 / 8 / 8.1 / 10 / 11
REM ============================================================

chcp 65001 >nul
title Network Watchdog Uninstaller
color 0E

REM ---- Administrator check ----
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo  [!] This script needs Administrator rights to remove the startup task.
    echo      Right-click this file and select "Run as administrator".
    echo.
    pause
    exit /b 1
)

echo.
echo  ============================================================
echo   Network Watchdog - Full Uninstall
echo  ============================================================
echo.
echo  This will remove:
echo    - Scheduled task      (NetworkWatchdog)
echo    - Legacy PS task      (NetworkWatchdog_AutoStart)
echo    - Registry startup entry
echo    - Settings folder     %%APPDATA%%\NetworkWatchdog
echo    - Log folder          %%LOCALAPPDATA%%\NetworkWatchdog
echo    - NetworkWatchdog.exe next to this script
echo.
set /p CONFIRM=Continue? [y/N]:
if /i not "%CONFIRM%"=="y" (
    echo  Aborted.
    pause
    exit /b 1
)

echo.
echo  Closing NetworkWatchdog if running...
taskkill /f /im NetworkWatchdog.exe >nul 2>&1

echo  Removing scheduled task...
schtasks /Delete /TN NetworkWatchdog /F >nul 2>&1
schtasks /Delete /TN NetworkWatchdog_AutoStart /F >nul 2>&1

echo  Removing registry startup entry...
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v NetworkWatchdog /f >nul 2>&1

echo  Removing settings and log folders...
rmdir /s /q "%APPDATA%\NetworkWatchdog" >nul 2>&1
rmdir /s /q "%LOCALAPPDATA%\NetworkWatchdog" >nul 2>&1

echo  Removing executable...
del "%~dp0NetworkWatchdog.exe" >nul 2>&1

echo.
echo  ============================================================
echo   Done. Network Watchdog has been removed.
echo  ============================================================
echo.
pause
