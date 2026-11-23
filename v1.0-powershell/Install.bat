@echo off
REM ============================================================
REM  نصب‌کننده Network Watchdog
REM  این فایل اسکریپت PowerShell را با دسترسی Administrator اجرا می‌کند
REM  و در اولین اجرا، خود را در استارتآپ ویندوز ثبت می‌کند.
REM ============================================================

chcp 65001 >nul
title Network Watchdog Installer
color 0A

echo.
echo  ============================================================
echo   Network Watchdog - مانیتور و بازیاب خودکار اینترنت
echo  ============================================================
echo.
echo  اين برنامه:
echo   - اتصال سيستم به اينترنت را هر 1 دقيقه بررسي مي‌كند
echo   - در صورت قطع اتصال، اينترفيس‌هاي شبکه را ري‌استارت مي‌كند
echo   - خود را در استارتاپ ويندوز ثبت مي‌كند تا هنگام روشن شدن
echo     سيستم به‌صورت خودکار اجرا شود
echo.
echo  توجه: براي مديريت آداپتور شبکه به دسترس Administrator نياز است.
echo  اگر درخواست UAC ظاهر شد، براي ادامه Yes را کليک کنيد.
echo.
pause

REM مسیر اسکریپت نسبت به همین فایل bat
set "SCRIPT_DIR=%~dp0"
set "PS_SCRIPT=%SCRIPT_DIR%NetworkWatchdog.ps1"

if not exist "%PS_SCRIPT%" (
    echo.
    echo  [خطا] فایل NetworkWatchdog.ps1 يافت نشد!
    echo  مطمئن شويد هر دو فایل در يک پوشه هستند.
    echo.
    pause
    exit /b 1
)

echo.
echo  در حال اجراي اسکريپت با دسترسي Administrator...
echo.

REM اجرای PowerShell با دسترسی Administrator
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File ""%PS_SCRIPT%""' -Verb RunAs"

echo.
echo  ============================================================
echo   نصب و راه‌اندازی کامل شد!
echo  ============================================================
echo.
echo  برنامه در حال اجراست و در استارتاپ ثبت شده است.
echo  فایل لاگ:  %%LOCALAPPDATA%%\NetworkWatchdog\watchdog.log
echo.
echo  براي حذف برنامه از استارتاپ، فایل Uninstall.bat را اجرا کنيد.
echo.
pause
