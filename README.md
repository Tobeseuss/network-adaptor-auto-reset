# Network Adaptor Auto Reset

[![Release](https://img.shields.io/github/v/release/Tobeseuss/network-adaptor-auto-reset)](https://github.com/Tobeseuss/network-adaptor-auto-reset/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%207%20%7C%208%20%7C%2010%20%7C%2011-blue)]()
[![.NET](https://img.shields.io/badge/.NET%20Framework-4.x-purple)]()

مانیتور و بازیاب خودکار اتصال اینترنت **با محافظت شبکه محلی دوربین‌ها** برای ویندوز

## سناریو و مشکل

سیستم‌هایی که هم به اینترنت متصل‌اند و هم تصاویر دوربین‌ها را از شبکه محلی دریافت می‌کنند، وقتی اینترنت قطع می‌شود، ابزارهای بازیابی شبکه با ری‌استارت کردن آداپتور، ارتباط با دوربین‌ها را هم مختل می‌کنند.

این برنامه با منطق هوشمند این مشکل را حل می‌کند:

| وضعیت | اقدام برنامه | آیکون کنار ساعت |
|---|---|---|
| اینترنت و شبکه محلی هر دو سالم | فقط پایش | سبز |
| اینترنت قطع، شبکه محلی (دوربین‌ها) سالم | **هیچ ری‌استارتی انجام نمی‌شود** (حفظ تصاویر دوربین‌ها) | نارنجی |
| اینترنت سالم، شبکه محلی قطع | ری‌استارت نمی‌کند (حفظ اینترنت) + هشدار در لاگ | نارنجی |
| قطع کامل (اینترنت + شبکه محلی) | ری‌استارت آداپتورهای شبکه (Disable سپس Enable) | قرمز |

## نسخه‌ها

| | v1.0.0 (PowerShell) | v2.0.0 (C#/.NET) - پیشنهادی |
|---|---|---|
| ویندوز | 8 / 8.1 / 10 / 11 | **7 SP1 / 8 / 8.1 / 10 / 11** |
| خروجی | اسکریپت ps1 | **EXE مستقل** (فقط 50KB) |
| آیکون کنار ساعت | ندارد | **دارد** (سبز/نارنجی/قرمز + اعلان‌ها) |
| محافظت شبکه دوربین‌ها | ندارد | **دارد** (پیش‌فرض `192.168.1.1` - قابل تغییر) |
| تنظیمات | ویرایش فایل | **پنجره تنظیمات فارسی** |
| استارتآپ خودکار | دارد | دارد (**بدون پنجره UAC** هنگام بوت) |
| جلوگیری از اجرای همزمان | - | دارد (Mutex) |

> نسخه v2.0.0 را مستقیم از بخش [Releases](../../releases) دانلود کنید - فایل `NetworkWatchdog.exe` آماده اجراست.

## نصب سریع (v2.0.0)

1. `NetworkWatchdog.exe` را از [Releases](../../releases) دانلود کنید
2. روی آن دوبار کلیک کنید (خودش درخواست UAC می‌دهد)
3. تمام! در اولین اجرا:
   - خود را در استارتآپ ویندوز ثبت می‌کند (از بوت بعدی بدون دخالت شما اجرا می‌شود)
   - آیکون سبز کنار ساعت ظاهر می‌شود
   - فایل تنظیمات با مقادیر پیش‌فرض ساخته می‌شود

## منطق تصمیم‌گیری برنامه

```
هر 60 ثانیه (قابل تغییر):
├── اینترنت وصل است؟ (Ping به 8.8.8.8 و ... + اتصال TCP پورت 53)
│   ├── بله + شبکه محلی وصل → سبز؛ ادامه پایش
│   ├── بله + شبکه محلی قطع → نارنجی؛ هشدار لاگ (ری‌استارت نمی‌کند تا اینترنت نشکند)
│   └── خیر ↓
├── شبکه محلی (پیش‌فرض 192.168.1.1) در دسترس است؟
│   ├── بله → نارنجی؛ ری‌استارت نمی‌شود (تصاویر دوربین‌ها حفظ می‌شود)
│   └── خیر (قطع کامل) → قرمز؛ ری‌استارت آداپتورها + بررسی مجدد
```

## تنظیمات

کلیک راست روی آیکون ← **تنظیمات...** (یا دابل‌کلیک روی آیکون)

| تنظیم | پیش‌فرض | توضیح |
|---|---|---|
| آدرس‌های بررسی اینترنت | `8.8.8.8, 1.1.1.1, 208.67.222.222` | سرورهای تست اینترنت |
| آدرس‌های شبکه محلی / دوربین‌ها | `192.168.1.1` | IP مودم/سوئیچ شبکه دوربین‌ها |
| محافظت شبکه محلی | فعال | اگر این IP وصل باشد هرگز ری‌استارت نکن |
| فاصله هر بررسی | 60 ثانیه | دوره بررسی |
| مهلت هر تست | 2000ms | Timeout هر Ping/TCP |
| تاخیر غیرفعال/فعال | 5 ثانیه | فاصله بین Disable و Enable |
| صبر بالا آمدن آداپتور | 10 ثانیه | بعد از Enable چقدر صبر کند |
| تعداد تلاش مجدد | 3 | برای هر آداپتور |
| الگوی حذف آداپتور مجازی | Regex پیش‌فرض | آداپتورهای VPN/VMware/Hyper-V حذف می‌شوند |

فایل تنظیمات: `%APPDATA%\NetworkWatchdog\settings.xml`
فایل لاگ: `%LOCALAPPDATA%\NetworkWatchdog\watchdog.log`

## ساخت از سورس

هر سه فایل `NetworkWatchdog.cs` و `app.manifest` و `app.ico` و `build.bat` را در یک پوشه بگذارید و `build.bat` را اجرا کنید - با کامپایلر داخلی خود ویندوز (csc.exe) کامپایل می‌شود و **نیاز به نصب Visual Studio یا SDK ندارید**.

## پیش‌نیازها

| سیستم‌عامل | نیازمندی |
|---|---|
| Windows 10 / 11 | هیچ چیز - .NET Framework 4.x داخل ویندوز است |
| Windows 8 / 8.1 | هیچ چیز |
| Windows 7 SP1 | در صورت نبود، یک‌بار [NET Framework 4.x.](https://dotnet.microsoft.com/download/dotnet-framework/net48) نصب کنید |

## حذف برنامه

1. کلیک راست روی آیکون ← خروج
2. در `Task Scheduler` تسک `NetworkWatchdog` را حذف کنید
3. پوشه‌های `%APPDATA%\NetworkWatchdog` و `%LOCALAPPDATA%\NetworkWatchdog` و فایل EXE را پاک کنید

## ساختار مخزن

```
v1.0-powershell/   →  نسخه PowerShell (ویندوز 8+)
v2.0-csharp/       →  نسخه C#/.NET (ویندوز 7 SP1+)
```

## نکات فنی

- مدیریت آداپتورها با **WMI** (`Win32_NetworkAdapter`) انجام می‌شود که از ویندوز ویستا تا 11 یکسان است؛ cmdletهای PowerShell مانند `Get-NetAdapter` فقط در ویندوز 8+ وجود دارند
- در صورت خطای WMI، خودکار به `netsh interface set interface` سوییچ می‌شود
- تست اینترنت دومرحله‌ای: Ping و در صورت فیلتر بودن ICMP، اتصال TCP به پورت 53
- استارتآپ با Scheduled Task با سطح دسترسی بالا ثبت می‌شود تا هنگام بوت بدون پنجره UAC اجرا شود
- فقط آداپتورهای فیزیکی و فعال ری‌استارت می‌شوند

## English

**Network Adaptor Auto Reset** is a Windows internet-connectivity watchdog. When both the internet AND the local network (e.g., the camera LAN gateway) are unreachable, it automatically restarts network adapters (disable/enable). If only the internet is down while the local camera network is healthy, it does NOT restart the network, so camera feeds stay alive. It runs as a system-tray icon (green/orange/red), auto-starts with Windows without a UAC prompt (scheduled task with highest privileges), and provides a Persian settings GUI. Compatible with Windows 7 SP1 through Windows 11 (.NET Framework 4.x, ~50 KB standalone EXE). v1.0.0 (PowerShell, Win8+) is also included in `v1.0-powershell/`.

## مجوز

[MIT](LICENSE)
