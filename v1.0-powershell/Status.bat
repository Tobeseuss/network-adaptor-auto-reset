@echo off
REM ============================================================
REM  نمایش وضعیت Network Watchdog و آخرین لاگ‌ها
REM ============================================================

chcp 65001 >nul
title Network Watchdog - Status
color 0B

echo.
echo  ============================================================
echo   وضعیت Network Watchdog
echo  ============================================================
echo.

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
    "Write-Host '--- آیا برنامه در حال اجراست؟ ---' -ForegroundColor Cyan;" ^
    "$procs = Get-Process powershell -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -match 'NetworkWatchdog' };" ^
    "if ($procs) { Write-Host ('در حال اجرا - PID: ' + ($procs.Id -join ', ')) -ForegroundColor Green } else { Write-Host 'در حال اجرا نيست' -ForegroundColor Yellow };" ^
    "Write-Host '';" ^
    "Write-Host '--- وضعیت استارتآپ ---' -ForegroundColor Cyan;" ^
    "$run = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'NetworkWatchdog' -ErrorAction SilentlyContinue;" ^
    "if ($run.NetworkWatchdog) { Write-Host 'رجيستري Run: ثبت شده ✓' -ForegroundColor Green } else { Write-Host 'رجيستري Run: ثبت نشده ✗' -ForegroundColor Red };" ^
    "$task = Get-ScheduledTask -TaskName 'NetworkWatchdog_AutoStart' -ErrorAction SilentlyContinue;" ^
    "if ($task) { Write-Host 'Scheduled Task: ثبت شده ✓' -ForegroundColor Green } else { Write-Host 'Scheduled Task: ثبت نشده ✗' -ForegroundColor Red };" ^
    "Write-Host '';" ^
    "Write-Host '--- اتصال اینترنت فعلی ---' -ForegroundColor Cyan;" ^
    "$ok = (Test-Connection -ComputerName 8.8.8.8 -Count 2 -Quiet -ErrorAction SilentlyContinue);" ^
    "if ($ok) { Write-Host 'اینترنت: متصل ✓' -ForegroundColor Green } else { Write-Host 'اینترنت: قطع ✗' -ForegroundColor Red };" ^
    "Write-Host '';" ^
    "Write-Host '--- آخرین 20 خط لاگ ---' -ForegroundColor Cyan;" ^
    "$log = Join-Path $env:LOCALAPPDATA 'NetworkWatchdog\watchdog.log';" ^
    "if (Test-Path $log) { Get-Content $log -Tail 20 } else { Write-Host 'فایل لاگ يافت نشد' -ForegroundColor Yellow };"

echo.
pause
