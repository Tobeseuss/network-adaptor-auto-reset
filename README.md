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

| | v1.x (PowerShell) | v2.x (C#/.NET) - پیشنهادی |
|---|---|---|
| ویندوز | 8 / 8.1 / 10 / 11 | **7 SP1 / 8 / 8.1 / 10 / 11** |
| خروجی | اسکریپت ps1 | **EXE مستقل** (حدود 70KB) |
| آیکون کنار ساعت | ندارد | **دارد** (سبز/نارنجی/قرمز + اعلان‌ها) |
| محافظت شبکه دوربین‌ها | ندارد | **دارد** (پیش‌فرض `192.168.1.1` - قابل تغییر) |
| تنظیمات | ویرایش فایل | **پنجره تنظیمات فارسی** |
| پنجره لاگ زنده | - | **دارد** (v2.2: نمایش لحظه‌ای فعالیت‌ها، نتایج و دستورات در حال اجرا) |
| انتخاب آداپتورهای تحت پایش/ری‌استارت | - | **دارد** (v2.3: در تنظیمات، با وضعیت لحظه‌ای در لاگ) |
| موتور ری‌استارت | PowerShell | **سه‌لایه**: netsh ← WMI ← PowerShell + تایید وضعیت (v2.2) |
| استارتآپ خودکار | دارد | دارد (**بدون پنجره UAC** هنگام بوت) |
| جلوگیری از اجرای همزمان | - | دارد (Mutex) |

> نسخه C# را مستقیم از بخش [Releases](../../releases) دانلود کنید - فایل `NetworkWatchdog.exe` آماده اجراست.

## نصب سریع (نسخه C#)

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

کلیک راست روی آیکون ← **تنظیمات...** — برای دیدن لاگ زنده: **پنجره لاگ زنده** یا دابل‌کلیک روی آیکون

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
| آداپتورهای انتخابی (v2.3) | خالی = همه | فهرست آداپتورهایی که ری‌استارت روی آنها اعمال می‌شود |
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

## حذف برنامه (Uninstall)

**روش 1 - از داخل خود برنامه (پیشنهادی):**

کلیک راست روی آیکون ← **حذف کامل برنامه (Uninstall)** ← پاسخ به دو سوال (تایید حذف + حذف یا نگه‌داشتن داده‌ها). برنامه خودش: استارتآپ را پاک می‌کند، آثار نسخه قبلی PowerShell را حذف می‌کند، بسته می‌شود و فایل EXE و در صورت انتخاب، پوشه‌های تنظیمات و لاگ را حذف می‌کند.

**روش 2 - اسکریپت مستقل (وقتی برنامه در حال اجرا نیست):**

فایل `Uninstall.bat` در پوشه `csharp/` را **Run as administrator** کنید.

**روش 3 - دستی:**

1. کلیک راست روی آیکون ← خروج
2. در `Task Scheduler` تسک `NetworkWatchdog` را حذف کنید
3. پوشه‌های `%APPDATA%\NetworkWatchdog` و `%LOCALAPPDATA%\NetworkWatchdog` و فایل EXE را پاک کنید

## ساختار مخزن

```
powershell/       →  نسخه 1.x PowerShell (ویندوز 8+)
csharp/            →  نسخه 2.x C#/.NET (ویندوز 7 SP1+) - نسخه اصلی و پیشنهادی
legacy-python/     →  اسکریپت پایتون RestartNetworkAdaptors.py (مرجع منطق ری‌استارت آداپتورها که در v2.2 با C# ادغام شد)
```

## تاریخچه نسخه‌ها

| نسخه | توضیح |
|---|---|
| [v2.3.1](../../releases/tag/v2.3.1) | **اصلاح باگ مهم**: الگوی «حذف آداپتورهای مجازی» خالی دیگر همه آداپتورها را حذف نمی‌کند (Regex خالی با همه رشته‌ها مطابقت می‌داد ← ری‌استارت با پیام «آداپتور یافت نشد» شکست می‌خورد) + بازیابی مقدار الگو هنگام باز شدن تنظیمات (قبلاً با ذخیره، ناخواسته خالی می‌شد) + دکمه «پیش‌فرض» |
| [v2.3.0](../../releases/tag/v2.3.0) | **انتخاب آداپتورهای تحت پایش و ری‌استارت** در تنظیمات (فهرست زنده با وضعیت فعال/غیرفعال؛ خالی = همه) + منوی «درباره برنامه» + درج «برنامه‌نویسی شده توسط کمالی» |
| [v2.2.0](../../releases/tag/v2.2.0) | **ادغام منطق اسکریپت پایتون ری‌استارت آداپتورها** (netsh اصلی ← WMI ← PowerShell + تایید وضعیت واقعی + خودترمیم آداپتورهای غیرفعال‌مانده) + **پنجره لاگ زنده** با نمایش لحظه‌ای همه فعالیت‌ها، نتایج و دستورات در حال اجرا |
| [v2.1.0](../../releases/tag/v2.1.0) | حذف کامل برنامه از منوی آیکون (Uninstall خودکار EXE و استارتآپ و داده‌ها) + اسکریپت Uninstall.bat مستقل + پاک‌سازی آثار نسخه PowerShell |
| [v2.0.0](../../releases/tag/v2.0.0) | بازنویسی با C#/.NET: محافظت شبکه دوربین‌ها، ویندوز 7-11، آیکون System Tray، تنظیمات گرافیکی، خروجی EXE |
| [v1.0.0](../../releases/tag/v1.0.0) | نسخه اولیه PowerShell |

## نکات فنی

- **موتور ری‌استارت (v2.2 - منطق اسکریپت پایتون RestartNetworkAdaptors)**: روش اصلی `netsh interface set interface ... admin=disable/enable` است؛ در صورت عدم موفقیت به‌ترتیب WMI (Win32_NetworkAdapter) و سپس `powershell Restart-NetAdapter` (ویندوز 8+) امتحان می‌شوند و در پایان وضعیت واقعی آداپتور تایید می‌شود
- **انتخاب آداپتور (v2.3)**: اگر در تنظیمات آداپتورهایی انتخاب شده باشند، ری‌استارت فقط روی همان‌ها اعمال می‌شود و بقیه در لاگ با دلیل «خارج از انتخاب کاربر» نادیده گرفته می‌شوند؛ وضعیت لحظه‌ای آداپتورهای انتخابی هم در هر چرخه در لاگ ثبت می‌شود
- کشف آداپتورها از دو منبع: WMI (فعال و غیرفعال) + خروجی `netsh interface show interface` - اگر آداپتوری در ری‌استارت قبلی غیرفعال مانده باشد، دوباره فعال می‌شود (خودترمیم)
- تست اینترنت دومرحله‌ای: Ping و در صورت فیلتر بودن ICMP، اتصال TCP به پورت 53
- استارتآپ با Scheduled Task با سطح دسترسی بالا ثبت می‌شود تا هنگام بوت بدون پنجره UAC اجرا شود
- آداپتورهای مجازی (VPN/VMware/Hyper-V/TAP) با Regex قابل تنظیم حذف می‌شوند (الگوی خالی = بدون حذف - v2.3.1)

## English

**Network Adaptor Auto Reset** is a Windows internet-connectivity watchdog. When both the internet AND the local network (e.g., the camera LAN gateway) are unreachable, it automatically restarts network adapters (disable/enable) using a three-layer engine (netsh → WMI → PowerShell Restart-NetAdapter, with real status verification after each restart — the same proven logic as the original `RestartNetworkAdaptors.py` script, which is kept in `legacy-python/`). If only the internet is down while the local camera network is healthy, it does NOT restart the network, so camera feeds stay alive. It runs as a system-tray icon (green/orange/red), auto-starts with Windows without a UAC prompt (scheduled task with highest privileges), provides a Persian settings GUI, a **live log window** that shows every action, result and command being executed in real time, and offers one-click in-app uninstall. Compatible with Windows 7 SP1 through Windows 11 (.NET Framework 4.x, ~70 KB standalone EXE). v1.x (PowerShell, Win8+) is also included in `powershell/`.

## مجوز

[MIT](LICENSE)

---

**برنامه‌نویسی شده توسط کمالی**
