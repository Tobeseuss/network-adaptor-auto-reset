<#
.SYNOPSIS
    Network Watchdog - مانیتور و بازیاب خودکار اتصال اینترنت

.DESCRIPTION
    این اسکریپت اتصال سیستم به اینترنت را بررسی می‌کند.
    اگر اینترنت در دسترس نباشد، هر 1 دقیقه اینترفیس‌های شبکه را
    غیرفعال و سپس فعال (ری‌استارت) می‌کند تا اتصال بازیابی شود.
    در اولین اجرا، خود را در استارتآپ ویندوز ثبت می‌کند تا
    هنگام روشن شدن سیستم به‌صورت خودکار اجرا شود.

.NOTES
    فایل: NetworkWatchdog.ps1
    نویسنده: Z.ai Code
    نیاز: اجرا با دسترسی Administrator (برای مدیریت آداپتور شبکه)
#>

param(
    [switch]$Started   # نشان‌دهنده اجرای خودکار (نه اولین بار)
)

# ============================================================
#  تنظیمات قابل تغییر
# ============================================================
$Config = @{
    CheckIntervalSeconds   = 60        # فاصله بین هر بررسی (1 دقیقه)
    RestartDelaySeconds    = 5         # تاخیر بین disable و enable
    MaxRestartRetries      = 3         # تعداد تلاش ری‌استارت در هر دور
    PingTargets            = @('8.8.8.8', '1.1.1.1', '208.67.222.222')
    HttpCheckUrl           = 'http://www.msftconnecttest.com/connecttest.txt'
    HttpExpectedText       = 'Microsoft Connect Test'
    LogFile                = Join-Path $env:LOCALAPPDATA 'NetworkWatchdog\watchdog.log'
    LogMaxSizeKB           = 512       # حداکثر اندازه لاگ قبل از چرخش
}

# ============================================================
#  مدیریت خودکار اجرای Administrator
# ============================================================
function Test-Administrator {
    $current = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($current)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-SelfElevation {
    if (-not (Test-Administrator)) {
        Write-Warning "برای مدیریت آداپتور شبکه به دسترسی Administrator نیاز است. در حال اجرای مجدد با دسترسی بالا..."
        $scriptPath = $PSCommandPath
        if ([string]::IsNullOrEmpty($scriptPath)) { return }
        $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$scriptPath`" -Started"
        try {
            Start-Process -FilePath 'powershell.exe' -ArgumentList $argList -Verb RunAs -WindowStyle Minimized
        } catch {
            Write-Error "اجرای مجدد با دسترسی Administrator ناموفق بود. لطفاً اسکریپت را به‌صورت دستی Run as Administrator کنید."
            Start-Sleep -Seconds 5
        }
        exit
    }
}

# ============================================================
#  ثبت در استارتآپ ویندوز
# ============================================================
function Register-Startup {
    <#
        ثبت خودکار در استارتآپ با دو روش برای اطمینان:
        1) کلید Run در رجیستری (HKCU) - هر کاربر
        2) Scheduled Task با تریگر AtLogon - پایدارتر
    #>
    $scriptPath = $PSCommandPath
    if (-not $scriptPath) { return }

    # --- روش 1: کلید Run در رجیستری ---
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $appName = 'NetworkWatchdog'
    $command = "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$scriptPath`" -Started"

    try {
        if (-not (Test-Path $runKey)) { New-Item -Path $runKey -Force | Out-Null }
        Set-ItemProperty -Path $runKey -Name $appName -Value $command -Force
        Write-Log "استارتآپ (رجیستری Run) با موفقیت ثبت شد." -Level 'INFO'
    } catch {
        Write-Log "ثبت استارتآپ در رجیستری ناموفق: $($_.Exception.Message)" -Level 'WARN'
    }

    # --- روش 2: Scheduled Task ---
    try {
        $taskName = 'NetworkWatchdog_AutoStart'
        $existing = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
        if ($existing) { Unregister-ScheduledTask -TaskName $taskName -Confirm:$false }

        $action = New-ScheduledTaskAction -Execute 'powershell.exe' `
            -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$scriptPath`" -Started"
        $trigger = New-ScheduledTaskTrigger -AtLogon
        $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -RunLevel Highest
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
            -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 0)

        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger `
            -Principal $principal -Settings $settings -Force | Out-Null
        Write-Log "تسک زمان‌بندی‌شده استارتآپ (AtLogon) با موفقیت ثبت شد." -Level 'INFO'
    } catch {
        Write-Log "ثبت Scheduled Task ناموفق: $($_.Exception.Message)" -Level 'WARN'
    }
}

# ============================================================
#  سیستم لاگ‌گیری با چرخش فایل
# ============================================================
function Write-Log {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet('INFO','WARN','ERROR','DEBUG')][string]$Level = 'INFO'
    )
    $logDir = Split-Path $Config.LogFile -Parent
    if (-not (Test-Path $logDir)) { New-Item -Path $logDir -ItemType Directory -Force | Out-Null }

    # چرخش لاگ اگر خیلی بزرگ شد
    if (Test-Path $Config.LogFile) {
        $size = (Get-Item $Config.LogFile).Length / 1KB
        if ($size -gt $Config.LogMaxSizeKB) {
            $archive = $Config.LogFile + '.bak'
            if (Test-Path $archive) { Remove-Item $archive -Force }
            Rename-Item -Path $Config.LogFile -NewName ([System.IO.Path]::GetFileName($archive)) -Force
        }
    }

    $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $line = "[$stamp] [$Level] $Message"
    Add-Content -Path $Config.LogFile -Value $line -Encoding UTF8

    # نمایش در کنسول فقط در حالت interactive
    if ($Host.UI -and $Host.UI.RawUI) {
        switch ($Level) {
            'ERROR' { Write-Host $line -ForegroundColor Red }
            'WARN'  { Write-Host $line -ForegroundColor Yellow }
            'INFO'  { Write-Host $line -ForegroundColor Green }
            'DEBUG' { Write-Host $line -ForegroundColor DarkGray }
        }
    }
}

# ============================================================
#  بررسی اتصال اینترنت
# ============================================================
function Test-InternetConnection {
    <#
        تست چندلایه:
        1) HTTP connectivity test رسمی مایکروسافت
        2) Ping چند هدف (با fallback)
        اگر حداقل یکی پاسخ داد => اتصال برقرار است
    #>

    # روش 1: HTTP test (معتبرترین)
    try {
        $response = Invoke-WebRequest -Uri $Config.HttpCheckUrl -UseBasicParsing -TimeoutSec 10 `
            -ErrorAction Stop
        if ($response.Content -match $Config.HttpExpectedText) {
            return $true
        }
    } catch {
        # ادامه به تست ping
    }

    # روش 2: Ping fallback
    foreach ($target in $Config.PingTargets) {
        try {
            $result = Test-Connection -ComputerName $target -Count 2 -Quiet -ErrorAction SilentlyContinue
            if ($result) { return $true }
        } catch {
            continue
        }
    }

    return $false
}

# ============================================================
#  ری‌استارت اینترفیس‌های شبکه فیزیکی
# ============================================================
function Restart-NetworkAdapters {
    <#
        اینترفیس‌های شبکه فیزیکی و فعال را پیدا کرده،
        disable و سپس enable می‌کند.
        آداپتورهای مجازی (VPN/VMware/Hyper-V) نادیده گرفته می‌شوند.
    #>
    Write-Log "شروع فرآیند ری‌استارت آداپتورهای شبکه..." -Level 'INFO'

    $adapters = Get-NetAdapter | Where-Object {
        $_.Status -eq 'Up' -and
        $_.InterfaceDescription -notmatch 'Loopback|Virtual|Tunnel|ISATAP|Teredo' -and
        $_.Name -notmatch 'Loopback'
    } | Sort-Object -Property ifIndex

    if (-not $adapters -or ($adapters | Measure-Object).Count -eq 0) {
        Write-Log "هیچ آداپتور شبکه فیزیکی فعال یافت نشد." -Level 'WARN'
        return $false
    }

    $totalCount = ($adapters | Measure-Object).Count
    Write-Log "تعداد آداپتور برای ری‌استارت: $totalCount" -Level 'INFO'

    $restarted = 0
    foreach ($adapter in $adapters) {
        $name = $adapter.Name
        Write-Log "در حال ری‌استارت آداپتور: $name ($($adapter.InterfaceDescription))" -Level 'INFO'

        for ($attempt = 1; $attempt -le $Config.MaxRestartRetries; $attempt++) {
            try {
                Write-Log "  تلاش $attempt - غیرفعال‌سازی $name..." -Level 'DEBUG'
                Disable-NetAdapter -Name $name -Confirm:$false -ErrorAction Stop
                Start-Sleep -Seconds $Config.RestartDelaySeconds

                Write-Log "  تلاش $attempt - فعال‌سازی $name..." -Level 'DEBUG'
                Enable-NetAdapter -Name $name -Confirm:$false -ErrorAction Stop

                Start-Sleep -Seconds 8

                $nowUp = Get-NetAdapter -Name $name -ErrorAction SilentlyContinue
                if ($nowUp -and $nowUp.Status -eq 'Up') {
                    Write-Log "  آداپتور $name با موفقیت ری‌استارت شد." -Level 'INFO'
                    $restarted++
                    break
                } else {
                    Write-Log "  آداپتور $name پس از ری‌استارت Up نیست. تلاش مجدد..." -Level 'WARN'
                }
            } catch {
                Write-Log "  خطا در ری‌استارت $name (تلاش $attempt): $($_.Exception.Message)" -Level 'WARN'
                Start-Sleep -Seconds 2
            }
        }
    }

    Write-Log "ری‌استارت کامل شد. تعداد موفق: $restarted از $totalCount" -Level 'INFO'
    return ($restarted -gt 0)
}

# ============================================================
#  حلقه اصلی مانیتورینگ
# ============================================================
function Start-WatchdogLoop {
    Write-Log "========================================" -Level 'INFO'
    Write-Log "شروع مانیتورینگ Network Watchdog" -Level 'INFO'
    Write-Log "فاصله بررسی: $($Config.CheckIntervalSeconds) ثانیه" -Level 'INFO'
    Write-Log "فایل لاگ: $($Config.LogFile)" -Level 'INFO'
    Write-Log "========================================" -Level 'INFO'

    $consecutiveFailures = 0

    while ($true) {
        try {
            $connected = Test-InternetConnection

            if ($connected) {
                if ($consecutiveFailures -gt 0) {
                    Write-Log "اتصال اینترنت بازیابی شد! (پس از $consecutiveFailures دور شکست)" -Level 'INFO'
                } else {
                    Write-Log "اتصال اینترنت برقرار است." -Level 'DEBUG'
                }
                $consecutiveFailures = 0
            } else {
                $consecutiveFailures++
                Write-Log "عدم دسترسی به اینترنت شناسایی شد! (دور $consecutiveFailures)" -Level 'WARN'
                Write-Log "شروع ری‌استارت آداپتورهای شبکه..." -Level 'WARN'

                $success = Restart-NetworkAdapters
                if ($success) {
                    Start-Sleep -Seconds 10
                    $recheck = Test-InternetConnection
                    if ($recheck) {
                        Write-Log "اتصال پس از ری‌استارت شبکه بازیابی شد!" -Level 'INFO'
                        $consecutiveFailures = 0
                    } else {
                        Write-Log "اتصال هنوز برقرار نیست. در دور بعدی دوباره تلاش می‌شود." -Level 'WARN'
                    }
                }
            }
        } catch {
            Write-Log "خطای غیرمنتظره در حلقه اصلی: $($_.Exception.Message)" -Level 'ERROR'
            Write-Log "ادامه عملیات پس از انتظار..." -Level 'ERROR'
        }

        Start-Sleep -Seconds $Config.CheckIntervalSeconds
    }
}

# ============================================================
#  نقطه ورود اصلی
# ============================================================

# فقط در اولین اجرا (بدون -Started) استارتآپ ثبت شود
if (-not $Started) {
    Write-Host "اولین اجرای Network Watchdog - ثبت در استارتآپ..." -ForegroundColor Cyan
    # اول فایل لاگ را آماده کن تا Write-Log کار کند
    $logDir = Split-Path $Config.LogFile -Parent
    if (-not (Test-Path $logDir)) { New-Item -Path $logDir -ItemType Directory -Force | Out-Null }
    Register-Startup
    Write-Host "ثبت استارتآپ کامل شد." -ForegroundColor Green
} else {
    # در اجرای خودکار هم مطمئن شو استارتآپ ثبت شده (در صورت حذف تصادفی)
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if (-not (Get-ItemProperty -Path $runKey -Name 'NetworkWatchdog' -ErrorAction SilentlyContinue)) {
        $logDir = Split-Path $Config.LogFile -Parent
        if (-not (Test-Path $logDir)) { New-Item -Path $logDir -ItemType Directory -Force | Out-Null }
        Register-Startup
    }
}

# اطمینان از دسترسی Administrator
Invoke-SelfElevation

# شروع حلقه مانیتورینگ
Start-WatchdogLoop
