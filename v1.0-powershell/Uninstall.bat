@echo off
REM ============================================================
REM  حذف‌کننده Network Watchdog از استارتآپ ویندوز
REM ============================================================

chcp 65001 >nul
title Network Watchdog Uninstaller
color 0E

echo.
echo  ============================================================
echo   حذف Network Watchdog از استارتاپ ويندوز
echo  ============================================================
echo.
echo  در حال حذف تنظيمات استارتاپ...
echo.

REM اجرای دستورات حذف با PowerShell (نیاز به Administrator ندارد برای HKCU)
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "$ErrorActionPreference='SilentlyContinue';" ^
    "Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'NetworkWatchdog' -Force;" ^
    "Unregister-ScheduledTask -TaskName 'NetworkWatchdog_AutoStart' -Confirm:$false;" ^
    "Write-Host 'استارتآپ رجيستري حذف شد.' -ForegroundColor Green;" ^
    "Write-Host 'تسک زمان‌بندي‌شده حذف شد.' -ForegroundColor Green;"

echo.
echo  ============================================================
echo   حذف کامل شد!
echo  ============================================================
echo.
echo  توجه:
echo   - اگر برنامه در حال اجراست، آن را از Task Manager ببنديد
echo     (پردازش powershell.exe که NetworkWatchdog.ps1 را اجرا مي‌کند)
echo   - فایل لاگ در مسير زير باقي مانده است و مي‌توانيد آن را
echo     به‌صورت دستي حذف کنيد:
echo       %%LOCALAPPDATA%%\NetworkWatchdog\
echo.
pause
