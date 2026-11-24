// ============================================================================
//  Network Watchdog  -  مانیتور و بازیاب خودکار اتصال اینترنت
//  + محافظت شبکه محلی دوربین‌ها (اگر شبکه محلی سالم باشد، ری‌استارت انجام نمی‌شود)
//
//  سازگار با:  Windows 7 SP1 / 8 / 8.1 / 10 / 11   (نیازمند .NET Framework 4.x)
//  کامپایل:    build.bat  (با کامپایلر داخلی csc.exe خود ویندوز - بدون نیاز به نصب)
//
//  منطق عملکرد:
//    1) هر N ثانیه اتصال اینترنت بررسی می‌شود (Ping + TCP fallback)
//    2) اگر اینترنت قطع ولی شبکه محلی (مثلاً 192.168.1.1) در دسترس بود،
//       شبکه ری‌استارت نمی‌شود (دریافت تصاویر دوربین‌ها حفظ می‌شود)
//    3) اگر هم اینترنت و هم شبکه محلی قطع بود، آداپتورها با منطق
//       اسکریپت پایتون RestartNetworkAdaptors ری‌استارت می‌شوند:
//       netsh (اصلی) -> WMI (جایگزین) -> PowerShell (آخرین) + تایید وضعیت
//    4) در اولین اجرا، خود را در استارتآپ ویندوز (Scheduled Task) ثبت می‌کند
//    5) آیکون کنار ساعت: سبز = سالم | نارنجی = فقط اینترنت قطع | قرمز = قطع کامل
//    6) پنجره «لاگ زنده»: نمایش لحظه‌ای اقدامات، نتایج و دستورات در حال اجرا
//    7) حذف کامل برنامه (Uninstall) از منوی آیکون: پاک کردن استارتآپ،
//       بستن برنامه، حذف EXE و در صورت درخواست، پاک کردن تنظیمات و لاگ
//    8) انتخاب آداپتورها (v2.3): در تنظیمات مشخص می‌کنید پایش و ری‌استارت
//       فقط روی کدام آداپتورها اعمال شود (خالی = همه آداپتورهای غیرمجازی)
//
//  برنامه‌نویسی شده توسط کمالی
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;

namespace NetworkWatchdog
{
    // ========================================================================
    //  اطلاعات نسخه
    // ========================================================================
    internal static class AppInfo
    {
        public const string Version = "2.3.0";
    }

    // ========================================================================
    //  تنظیمات برنامه - در %APPDATA%\NetworkWatchdog\settings.xml ذخیره می‌شود
    // ========================================================================
    public class AppSettings
    {
        public int CheckIntervalSeconds { get; set; }     // فاصله هر بررسی (ثانیه)
        public int PingTimeoutMs { get; set; }            // timeout هر تست
        public int TcpFallbackPort { get; set; }          // پورت TCP جایگزین برای تست اینترنت
        public string InternetTargets { get; set; }       // آدرس‌های تست اینترنت
        public string LocalTargets { get; set; }          // آدرس‌های شبکه محلی (دوربین/روتر)
        public bool ProtectLocalNetwork { get; set; }     // محافظت شبکه محلی
        public bool ShowBalloons { get; set; }            // نمایش اعلان‌ها
        public bool RunAtStartup { get; set; }            // اجرای خودکار هنگام روشن شدن
        public int RestartDelaySeconds { get; set; }      // تاخیر بین disable و enable
        public int AdapterUpWaitSeconds { get; set; }     // صبر برای بالا آمدن آداپتور
        public int MaxRestartRetries { get; set; }        // تعداد تلاش مجدد هر آداپتور
        public string AdapterExcludePattern { get; set; } // الگوی حذف آداپتورهای مجازی
        public int LogMaxSizeKB { get; set; }             // حداکثر حجم لاگ
        public string MonitoredAdapters { get; set; }     // آداپتورهای انتخابی کاربر (خالی = همه)

        public AppSettings()
        {
            CheckIntervalSeconds = 60;
            PingTimeoutMs = 2000;
            TcpFallbackPort = 53;
            InternetTargets = "8.8.8.8, 1.1.1.1, 208.67.222.222";
            LocalTargets = "192.168.1.1";
            ProtectLocalNetwork = true;
            ShowBalloons = true;
            RunAtStartup = true;
            RestartDelaySeconds = 5;
            AdapterUpWaitSeconds = 10;
            MaxRestartRetries = 3;
            AdapterExcludePattern = "Virtual|VMware|VirtualBox|Hyper-V|TAP|Tunnel|ISATAP|Teredo|Loopback|WAN Miniport|NDISWAN|Kernel Debug|QoS Packet Scheduler";
            LogMaxSizeKB = 512;
            MonitoredAdapters = "";
        }

        // لیست‌ها از رشته‌های comma-separated ساخته می‌شوند (کامای فارسی هم پشتیبانی می‌شود)
        public string[] GetInternetTargets()
        {
            return SplitTargets(InternetTargets);
        }

        public string[] GetLocalTargets()
        {
            return SplitTargets(LocalTargets);
        }

        // آداپتورهای انتخاب‌شده برای پایش/ری‌استارت (خالی = همه آداپتورهای غیرمجازی)
        public string[] GetMonitoredAdapters()
        {
            return SplitTargets(MonitoredAdapters);
        }

        static string[] SplitTargets(string s)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrEmpty(s)) return result.ToArray();
            string[] parts = s.Split(new char[] { ',', ';', '\u060C' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string p in parts)
            {
                string t = p.Trim();
                if (t.Length > 0) result.Add(t);
            }
            return result.ToArray();
        }

        public AppSettings Clone()
        {
            AppSettings c = new AppSettings();
            c.CheckIntervalSeconds = CheckIntervalSeconds;
            c.PingTimeoutMs = PingTimeoutMs;
            c.TcpFallbackPort = TcpFallbackPort;
            c.InternetTargets = InternetTargets;
            c.LocalTargets = LocalTargets;
            c.ProtectLocalNetwork = ProtectLocalNetwork;
            c.ShowBalloons = ShowBalloons;
            c.RunAtStartup = RunAtStartup;
            c.RestartDelaySeconds = RestartDelaySeconds;
            c.AdapterUpWaitSeconds = AdapterUpWaitSeconds;
            c.MaxRestartRetries = MaxRestartRetries;
            c.AdapterExcludePattern = AdapterExcludePattern;
            c.LogMaxSizeKB = LogMaxSizeKB;
            c.MonitoredAdapters = MonitoredAdapters;
            return c;
        }
    }

    // ========================================================================
    //  ذخیره/بازیابی تنظیمات در XML
    // ========================================================================
    public static class SettingsStore
    {
        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NetworkWatchdog");
        public static readonly string FilePath = Path.Combine(Dir, "settings.xml");

        public static bool Exists()
        {
            return File.Exists(FilePath);
        }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    XmlSerializer xs = new XmlSerializer(typeof(AppSettings));
                    using (FileStream fs = File.OpenRead(FilePath))
                    {
                        object obj = xs.Deserialize(fs);
                        AppSettings s = obj as AppSettings;
                        if (s != null) return s;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Write("WARN", "خطا در خواندن تنظیمات: " + ex.Message);
            }
            return new AppSettings();
        }

        public static void Save(AppSettings s)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                XmlSerializer xs = new XmlSerializer(typeof(AppSettings));
                using (StreamWriter sw = new StreamWriter(FilePath, false, Encoding.UTF8))
                {
                    xs.Serialize(sw, s);
                }
            }
            catch (Exception ex)
            {
                Logger.Write("ERROR", "خطا در ذخیره تنظیمات: " + ex.Message);
            }
        }
    }

    // ========================================================================
    //  سیستم لاگ‌گیری با چرخش خودکار فایل
    // ========================================================================
    public static class Logger
    {
        static readonly object SyncRoot = new object();
        static string _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NetworkWatchdog", "watchdog.log");
        static int _maxKb = 512;

        public static string FilePath
        {
            get { return _path; }
        }

        public static string Dir
        {
            get { return Path.GetDirectoryName(_path); }
        }

        public static void Configure(int maxKb)
        {
            if (maxKb > 0) _maxKb = maxKb;
        }

        // رخداد «خط ثبت‌شده» - پنجره لاگ زنده برای به‌روزرسانی لحظه‌ای مشترک می‌شود
        public static event Action<string> LineLogged;

        public static void Write(string level, string msg)
        {
            string line = null;
            lock (SyncRoot)
            {
                try
                {
                    string dir = Path.GetDirectoryName(_path);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    try
                    {
                        FileInfo fi = new FileInfo(_path);
                        if (fi.Exists && fi.Length > (long)_maxKb * 1024)
                        {
                            string bak = _path + ".bak";
                            if (File.Exists(bak)) File.Delete(bak);
                            File.Move(_path, bak);
                        }
                    }
                    catch { }
                    line = string.Format("[{0}] [{1}] {2}",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), level, msg);
                    File.AppendAllText(_path, line + "\r\n", Encoding.UTF8);
                }
                catch { }
            }
            // اطلاع‌رسانی به مشترکان (پنجره لاگ زنده) خارج از قفل فایل
            if (line != null)
            {
                Action<string> handler = LineLogged;
                if (handler != null)
                {
                    try { handler(line); }
                    catch { }
                }
            }
        }
    }

    // ========================================================================
    //  فعالیت جاری برنامه - در نوار وضعیت پنجره لاگ زنده نمایش داده می‌شود
    //  (مثلاً: «در حال بررسی اتصال»، «ری‌استارت آداپتور Ethernet»، ...)
    // ========================================================================
    public static class Activity
    {
        static string _current = "";

        public static string Current
        {
            get { return _current; }
        }

        public static event Action<string> Changed;

        public static void Set(string text)
        {
            _current = text == null ? "" : text;
            Action<string> handler = Changed;
            if (handler != null)
            {
                try { handler(_current); }
                catch { }
            }
        }
    }

    // ========================================================================
    //  بررسی اتصال:  Ping + TCP fallback (روی Win7/10/11 بدون وابستگی)
    // ========================================================================
    public static class NetCheck
    {
        public static bool IsReachable(string host, int timeoutMs, int tcpPort)
        {
            if (PingOk(host, timeoutMs)) return true;
            if (tcpPort > 0) return TcpOk(host, tcpPort, timeoutMs);
            return false;
        }

        public static bool AnyReachable(string[] hosts, int timeoutMs, int tcpPort)
        {
            if (hosts == null) return false;
            foreach (string h in hosts)
            {
                string t = h == null ? "" : h.Trim();
                if (t.Length == 0) continue;
                if (IsReachable(t, timeoutMs, tcpPort)) return true;
            }
            return false;
        }

        static bool PingOk(string host, int timeout)
        {
            try
            {
                using (Ping p = new Ping())
                {
                    PingReply r = p.Send(host, timeout);
                    if (r.Status == IPStatus.Success)
                    {
                        Logger.Write("DEBUG", "   Ping " + host + " -> پاسخ در " + r.RoundtripTime + "ms");
                        return true;
                    }
                    Logger.Write("DEBUG", "   Ping " + host + " -> " + r.Status);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Write("DEBUG", "   Ping " + host + " -> خطا: " + ex.GetType().Name);
                return false;
            }
        }

        // TCP connect با timeout - برای مواقعی که ICMP فیلتر شده است
        static bool TcpOk(string host, int port, int timeout)
        {
            TcpClient tc = null;
            try
            {
                tc = new TcpClient();
                IAsyncResult ar = tc.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeout, false))
                {
                    Logger.Write("DEBUG", "   TCP " + host + ":" + port + " -> مهلت تمام شد");
                    return false;
                }
                try
                {
                    tc.EndConnect(ar);
                    Logger.Write("DEBUG", "   TCP " + host + ":" + port + " -> اتصال برقرار شد");
                    return true;
                }
                catch
                {
                    Logger.Write("DEBUG", "   TCP " + host + ":" + port + " -> اتصال رد شد");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Write("DEBUG", "   TCP " + host + ":" + port + " -> خطا: " + ex.GetType().Name);
                return false;
            }
            finally
            {
                if (tc != null)
                {
                    try { tc.Close(); } catch { }
                }
            }
        }
    }

    // ========================================================================
    //  مدیریت آداپتورهای شبکه - منطق ادغام‌شده از اسکریپت پایتون
    //  RestartNetworkAdaptors.py کاربر:
    //
    //    کشف آداپتورها (مثل پایتون، از دو منبع):
    //      WMI   → همه آداپتورهای دارای «نام اتصال» (فعال و غیرفعال)
    //      netsh → خروجی «netsh interface show interface» (شامل غیرفعال‌ها؛
    //              اگر آداپتوری در ری‌استارت قبلی غیرفعال مانده باشد،
    //              دوباره فعال می‌شود - خودترمیم)
    //
    //    ری‌استارت (ترتیب دقیقاً مثل پایتون):
    //      1) netsh disable -> تاخیر -> netsh enable -> تایید وضعیت (اصلی)
    //      2) WMI Win32_NetworkAdapter.Disable/Enable (جایگزین)
    //      3) PowerShell Restart-NetAdapter (آخرین - ویندوز 8+)
    //
    //    تفاوت مهم با نسخه قبلی این برنامه: قبلاً WMI روش اصلی بود که
    //    روی بسیاری از سیستم‌ها کد موفق برمی‌گرداند ولی عملاً کاری انجام
    //    نمی‌دهد؛ اکنون netsh (روش اثبات‌شده اسکریپت پایتون) روش اصلی است
    //    و بعد از هر ری‌استارت، وضعیت واقعی آداپتور تایید می‌شود.
    //    همه دستورات و خروجی‌ها در لاگ (و پنجره لاگ زنده) ثبت می‌شوند.
    // ========================================================================
    public static class AdapterManager
    {
        public class AdapterInfo
        {
            public string Name;         // نام دستگاه (WMI Name)
            public string Description;  // توضیح/درایور آداپتور
            public string ConnectionId; // نام اتصال (همان نام در netsh و ncpa.cpl)
            public string DeviceId;     // شماره WMI DeviceID (برای روش جایگزین WMI)
            public bool Enabled;        // وضعیت فعلی آداپتور
        }

        public class CmdResult
        {
            public int ExitCode = -1;
            public string Output = "";
        }

        // ------------------------------------------------------------------
        //  کشف همه آداپتورهای غیرمجازی (ترکیب WMI + netsh - مطابق اسکریپت پایتون)
        //  (برای فهرست تنظیمات و به‌عنوان ورودی فیلتر انتخاب کاربر)
        // ------------------------------------------------------------------
        public static List<AdapterInfo> GetAllAdapters(string excludePattern)
        {
            Dictionary<string, AdapterInfo> map =
                new Dictionary<string, AdapterInfo>(StringComparer.OrdinalIgnoreCase);
            Regex rx = BuildExcludeRegex(excludePattern);

            // ---- منبع 1: WMI (یونیکد و مستقل از زبان ویندوز) ----
            try
            {
                int wmiCount = 0;
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        string name = Convert.ToString(mo["Name"]);
                        string desc = Convert.ToString(mo["Description"]);
                        string connId = Convert.ToString(mo["NetConnectionID"]);
                        string devId = Convert.ToString(mo["DeviceID"]);
                        if (connId == null || connId.Length == 0) continue;
                        if (rx.IsMatch(name) || rx.IsMatch(desc) || rx.IsMatch(connId)) continue;
                        bool enabled = false;
                        try { enabled = Convert.ToBoolean(mo["NetEnabled"]); }
                        catch { }
                        wmiCount++;
                        AdapterInfo ai;
                        if (map.TryGetValue(connId, out ai))
                        {
                            ai.Enabled = ai.Enabled || enabled;
                            if (ai.DeviceId.Length == 0) ai.DeviceId = devId;
                        }
                        else
                        {
                            ai = new AdapterInfo();
                            ai.Name = name;
                            ai.Description = desc;
                            ai.ConnectionId = connId;
                            ai.DeviceId = devId;
                            ai.Enabled = enabled;
                            map[connId] = ai;
                        }
                    }
                }
                Logger.Write("DEBUG", "منبع WMI: " + wmiCount + " آداپتور (پس از حذف مجازی‌ها) یافت شد.");
            }
            catch (Exception ex)
            {
                Logger.Write("WARN", "کشف آداپتور از WMI ناموفق: " + ex.Message);
            }

            // ---- منبع 2: netsh (مثل اسکریپت پایتون - شامل غیرفعال‌ها) ----
            try
            {
                List<string> netshNames = ParseNetshInterfaceList();
                int newCount = 0;
                foreach (string nm in netshNames)
                {
                    if (nm == null || nm.Length == 0) continue;
                    if (rx.IsMatch(nm)) continue;
                    if (!map.ContainsKey(nm))
                    {
                        AdapterInfo ai = new AdapterInfo();
                        ai.Name = nm;
                        ai.Description = "";
                        ai.ConnectionId = nm;
                        ai.DeviceId = "";
                        ai.Enabled = false;   // وضعیت از netsh معلوم نیست
                        map[nm] = ai;
                        newCount++;
                    }
                }
                Logger.Write("DEBUG", "منبع netsh: " + netshNames.Count + " نام اینترفیس خوانده شد (" +
                    newCount + " مورد جدید).");
            }
            catch (Exception ex)
            {
                Logger.Write("WARN", "کشف آداپتور از netsh ناموفق: " + ex.Message);
            }

            return new List<AdapterInfo>(map.Values);
        }

        // ------------------------------------------------------------------
        //  اعمال انتخاب کاربر روی فهرست آداپتورها (v2.3):
        //  اگر لیست انتخاب خالی باشد → همه آداپتورهای غیرمجازی هدف قرار
        //  می‌گیرند (رفتار پیش‌فرض). در غیر این صورت فقط موارد انتخابی.
        // ------------------------------------------------------------------
        public static List<AdapterInfo> GetResetTargets(string excludePattern, string[] selected)
        {
            List<AdapterInfo> all = GetAllAdapters(excludePattern);
            if (selected == null || selected.Length == 0)
            {
                Logger.Write("DEBUG", "انتخاب آداپتور: هیچ انتخابی ثبت نشده - همه آداپتورهای غیرمجازی هدف هستند.");
                return all;
            }

            List<AdapterInfo> result = new List<AdapterInfo>();
            foreach (AdapterInfo a in all)
            {
                bool isSelected = false;
                foreach (string s in selected)
                {
                    if (string.Compare(s, a.ConnectionId, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        isSelected = true;
                        break;
                    }
                }
                if (isSelected) result.Add(a);
                else Logger.Write("DEBUG", "آداپتور «" + a.ConnectionId +
                    "» نادیده گرفته شد (خارج از انتخاب کاربر).");
            }

            // هشدار برای انتخاب‌هایی که در سیستم وجود ندارند
            foreach (string s in selected)
            {
                bool found = false;
                foreach (AdapterInfo a in all)
                {
                    if (string.Compare(s, a.ConnectionId, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    Logger.Write("WARN", "آداپتور انتخاب‌شده «" + s + "» در سیستم یافت نشد!");
            }
            return result;
        }

        // ------------------------------------------------------------------
        //  ثبت وضعیت لحظه‌ای آداپتورهای انتخابی در لاگ (پایش شفاف - v2.3)
        // ------------------------------------------------------------------
        public static void LogSelectedStatus(string[] selected)
        {
            if (selected == null || selected.Length == 0) return;
            try
            {
                Dictionary<string, bool> states =
                    new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT NetConnectionID, NetEnabled FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        string connId = Convert.ToString(mo["NetConnectionID"]);
                        if (connId == null || connId.Length == 0) continue;
                        try { states[connId] = Convert.ToBoolean(mo["NetEnabled"]); }
                        catch { }
                    }
                }
                StringBuilder sb = new StringBuilder();
                foreach (string s in selected)
                {
                    bool enabled;
                    if (states.TryGetValue(s, out enabled))
                        sb.Append("«").Append(s).Append("»=").Append(enabled ? "فعال" : "غیرفعال").Append("  ");
                    else
                        sb.Append("«").Append(s).Append("»=یافت‌نشد  ");
                }
                string text = sb.ToString().TrimEnd();
                if (text.Length > 0)
                    Logger.Write("INFO", "وضعیت آداپتورهای انتخابی (پایش): " + text);
            }
            catch (Exception ex)
            {
                Logger.Write("DEBUG", "خواندن وضعیت آداپتورهای انتخابی ناموفق: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        //  ری‌استارت یک آداپتور - دقیقاً مطابق اسکریپت پایتون:
        //    1) netsh disable -> تاخیر -> netsh enable -> تایید وضعیت
        //    2) در صورت شکست: WMI Disable/Enable
        //    3) در صورت شکست: PowerShell Restart-NetAdapter (ویندوز 8+)
        //  همه دستورات اجراشده با کد خروج و خروجی متنی در لاگ ثبت می‌شوند
        // ------------------------------------------------------------------
        public static bool RestartAdapter(AdapterInfo a, AppSettings s)
        {
            int delaySec = Math.Max(1, s.RestartDelaySeconds);
            string okMethod = "";

            // ---------- روش 1: netsh (اصلی - مطابق اسکریپت پایتون) ----------
            try
            {
                Logger.Write("INFO", "-> روش 1 (netsh): غیرفعال‌سازی آداپتور...");
                CmdResult r1 = RunNetsh(a.ConnectionId, "disable");
                LogCmdResult("netsh disable", r1);

                Logger.Write("INFO", "-> انتظار " + delaySec + " ثانیه (آداپتور خاموش است)...");
                Thread.Sleep(delaySec * 1000);

                Logger.Write("INFO", "-> روش 1 (netsh): فعال‌سازی آداپتور...");
                CmdResult r2 = RunNetsh(a.ConnectionId, "enable");
                LogCmdResult("netsh enable", r2);

                Logger.Write("INFO", "-> انتظار 3 ثانیه برای بالا آمدن آداپتور...");
                Thread.Sleep(3000);

                if (VerifyEnabled(a, "netsh")) okMethod = "netsh";
                else Logger.Write("WARN", "-> روش netsh موفق نبود؛ روش جایگزین امتحان می‌شود.");
            }
            catch (Exception ex)
            {
                Logger.Write("WARN", "-> خطا در روش netsh: " + ex.Message);
            }

            // ---------- روش 2: WMI Win32_NetworkAdapter (جایگزین) ----------
            if (okMethod.Length == 0 && a.DeviceId.Length > 0)
            {
                try
                {
                    Logger.Write("INFO", "-> روش 2 (WMI): فراخوانی Win32_NetworkAdapter.Disable ...");
                    uint d = InvokeWmi(a.DeviceId, "Disable");
                    Logger.Write("INFO", "   نتیجه Disable: " + WmiResultText(d));
                    Thread.Sleep(delaySec * 1000);
                    Logger.Write("INFO", "-> روش 2 (WMI): فراخوانی Win32_NetworkAdapter.Enable ...");
                    uint e = InvokeWmi(a.DeviceId, "Enable");
                    Logger.Write("INFO", "   نتیجه Enable: " + WmiResultText(e));
                    Thread.Sleep(3000);
                    if (VerifyEnabled(a, "WMI")) okMethod = "WMI";
                    else Logger.Write("WARN", "-> روش WMI موفق نبود؛ روش بعدی امتحان می‌شود.");
                }
                catch (Exception ex)
                {
                    Logger.Write("WARN", "-> خطا در روش WMI: " + ex.Message);
                }
            }

            // ---------- روش 3: PowerShell Restart-NetAdapter (ویندوز 8+) ----------
            if (okMethod.Length == 0)
            {
                try
                {
                    Logger.Write("INFO", "-> روش 3 (PowerShell): Restart-NetAdapter (فقط ویندوز 8 به بعد)...");
                    string psCmd = "Restart-NetAdapter -Name '" + a.ConnectionId.Replace("'", "''") +
                        "' -Confirm:$false";
                    Logger.Write("INFO", "اجرای دستور: powershell -NoProfile -Command \"" + psCmd + "\"");
                    CmdResult rp = RunCapture("powershell",
                        "-NoProfile -ExecutionPolicy Bypass -Command \"" + psCmd + "\"", 90000);
                    LogCmdResult("PowerShell Restart-NetAdapter", rp);
                    Thread.Sleep(3000);
                    if (VerifyEnabled(a, "PowerShell")) okMethod = "PowerShell";
                }
                catch (Exception ex)
                {
                    Logger.Write("WARN", "-> خطا در روش PowerShell: " + ex.Message);
                }
            }

            if (okMethod.Length > 0)
            {
                Logger.Write("INFO", "آداپتور \"" + a.ConnectionId + "\" با موفقیت ری‌استارت شد (روش: " +
                    okMethod + ").");
                return true;
            }
            Logger.Write("ERROR", "ری‌استارت آداپتور \"" + a.ConnectionId + "\" با هیچ روشی موفق نشد!");
            return false;
        }

        // ------------------------------------------------------------------
        //  تایید فعال بودن آداپتور پس از ری‌استارت
        //  (WMI = معتبرترین و مستقل از زبان ویندوز / متن netsh = کلیدواژه انگلیسی)
        // ------------------------------------------------------------------
        static bool VerifyEnabled(AdapterInfo a, string source)
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT DeviceID, NetConnectionID, NetEnabled FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        string devId = Convert.ToString(mo["DeviceID"]);
                        string connId = Convert.ToString(mo["NetConnectionID"]);
                        bool match = false;
                        if (a.DeviceId.Length > 0 && devId == a.DeviceId) match = true;
                        else if (string.Compare(connId, a.ConnectionId, StringComparison.OrdinalIgnoreCase) == 0)
                            match = true;
                        if (!match) continue;
                        bool enabled = false;
                        try { enabled = Convert.ToBoolean(mo["NetEnabled"]); }
                        catch { }
                        if (enabled)
                        {
                            Logger.Write("INFO", "   تایید وضعیت (" + source + "): آداپتور \"" +
                                a.ConnectionId + "\" فعال است (WMI: NetEnabled=TRUE).");
                            return true;
                        }
                        Logger.Write("DEBUG", "   وضعیت فعلی (WMI): غیرفعال - تایید نشد.");
                        return false;   // آداپتور پیداست ولی هنوز غیرفعال است
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Write("DEBUG", "   تایید با WMI ناموفق: " + ex.Message);
            }
            // منبع دوم: متن خروجی netsh (مثل اسکریپت پایتون - کلیدواژه انگلیسی)
            try
            {
                Logger.Write("INFO", "اجرای دستور: netsh interface show interface name=\"" +
                    a.ConnectionId + "\"");
                CmdResult r = RunCapture("netsh",
                    "interface show interface name=\"" + a.ConnectionId + "\"", 15000);
                Logger.Write("DEBUG", "   خروجی کامل: " + Flatten(r.Output));
                if (r.Output != null && (r.Output.Contains("Connected") || r.Output.Contains("Enabled")))
                {
                    Logger.Write("INFO", "   تایید وضعیت (" + source +
                        "): متن netsh حاوی Connected/Enabled است.");
                    return true;
                }
            }
            catch { }
            return false;
        }

        static uint InvokeWmi(string deviceId, string method)
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapter WHERE DeviceID='" + deviceId + "'"))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    object result = mo.InvokeMethod(method, null);
                    if (result == null) return 0;
                    return Convert.ToUInt32(result);
                }
            }
            return 4294967295;   // آداپتور یافت نشد
        }

        static string WmiResultText(uint code)
        {
            if (code == 0) return "موفق (کد 0)";
            if (code == 2) return "دسترسی رد شد (کد 2)";
            return "کد " + code;
        }

        static Regex BuildExcludeRegex(string excludePattern)
        {
            try
            {
                return new Regex(excludePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch
            {
                return new Regex("Virtual|Loopback|Tunnel", RegexOptions.IgnoreCase);
            }
        }

        static CmdResult RunNetsh(string connectionId, string action)
        {
            Logger.Write("INFO", "اجرای دستور: netsh interface set interface name=\"" +
                connectionId + "\" admin=" + action);
            return RunCapture("netsh", string.Format(
                "interface set interface name=\"{0}\" admin={1}", connectionId, action), 30000);
        }

        // اجرای یک برنامه و گرفتن کد خروج + متن خروجی (برای ثبت دقیق در لاگ)
        static CmdResult RunCapture(string exe, string args, int timeoutMs)
        {
            CmdResult r = new CmdResult();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                try
                {
                    // خروجی برنامه‌های کنسول (netsh/powershell) با کدپیج OEM نوشته می‌شود
                    psi.StandardOutputEncoding =
                        Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
                    psi.StandardErrorEncoding = psi.StandardOutputEncoding;
                }
                catch { }
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    string error = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); }
                        catch { }
                    }
                    r.Output = output == null ? "" : output;
                    if (error != null && error.Trim().Length > 0)
                        r.Output = r.Output + "[STDERR] " + error;
                    r.ExitCode = p.HasExited ? p.ExitCode : -1;
                }
            }
            catch (Exception ex)
            {
                r.Output = "خطا در اجرا: " + ex.Message;
                r.ExitCode = -1;
            }
            return r;
        }

        // خلاصه‌سازی نتیجه یک دستور برای لاگ
        static void LogCmdResult(string title, CmdResult r)
        {
            string text = Flatten(r.Output);
            if (text.Length == 0)
                Logger.Write("INFO", "   کد خروج " + title + ": " + r.ExitCode + " (بدون خروجی متنی)");
            else
                Logger.Write("INFO", "   کد خروج " + title + ": " + r.ExitCode + " | خروجی: " + text);
        }

        static string Flatten(string s)
        {
            if (s == null) return "";
            string t = s.Trim();
            if (t.Length > 400) t = t.Substring(0, 400) + " ...";
            return t.Replace("\r\n", " | ").Replace("\n", " | ");
        }

        // خواندن لیست همه اینترفیس‌ها از netsh (شامل غیرفعال‌ها - مثل اسکریپت پایتون)
        static List<string> ParseNetshInterfaceList()
        {
            List<string> names = new List<string>();
            Logger.Write("INFO", "اجرای دستور: netsh interface show interface");
            CmdResult r = RunCapture("netsh", "interface show interface", 15000);
            LogCmdResult("netsh show interface", r);
            if (r.Output == null) return names;
            string[] lines = r.Output.Replace("\r\n", "\n").Split('\n');
            bool headerSkipped = false;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("-")) continue;                    // خط جداکننده
                if (!headerSkipped) { headerSkipped = true; continue; }  // سرستون (هر زبان)
                string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;
                // ستون‌ها: AdminState  State  Type  Name...  → نام = بقیه خط
                string name = string.Join(" ", parts, 3, parts.Length - 3);
                if (name.Length > 0) names.Add(name);
            }
            return names;
        }
    }

    // ========================================================================
    //  ثبت/حذف استارتآپ:  Scheduled Task (اصلی) + Registry Run (جایگزین)
    // ========================================================================
    public static class StartupManager
    {
        public const string TaskName = "NetworkWatchdog";
        public const string RegistryValueName = "NetworkWatchdog";

        public static string ExePath
        {
            get { return Assembly.GetExecutingAssembly().Location; }
        }

        public static void SetStartup(bool enable)
        {
            if (enable)
            {
                int code = RunCmd("schtasks", string.Format(
                    "/Create /TN {0} /TR \"{1}\" /SC ONLOGON /RL HIGHEST /F",
                    TaskName, ExePath));
                if (code == 0)
                {
                    Logger.Write("INFO", "تسک استارتآپ (Logon Trigger) با موفقیت ثبت شد.");
                    return;
                }
                Logger.Write("WARN", "ثبت تسک ناموفق (کد " + code + ") - استفاده از رجیستری Run.");
                SetRegistry(true);
            }
            else
            {
                RunCmd("schtasks", "/Delete /TN " + TaskName + " /F");
                SetRegistry(false);
                Logger.Write("INFO", "اجرای خودکار از استارتآپ حذف شد.");
            }
        }

        public static bool IsStartupEnabled()
        {
            if (RunCmd("schtasks", "/Query /TN " + TaskName) == 0) return true;
            try
            {
                Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    "Software\\Microsoft\\Windows\\CurrentVersion\\Run", false);
                if (k != null)
                {
                    object v = k.GetValue(RegistryValueName);
                    k.Close();
                    return v != null;
                }
            }
            catch { }
            return false;
        }

        static void SetRegistry(bool on)
        {
            try
            {
                Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    "Software\\Microsoft\\Windows\\CurrentVersion\\Run");
                if (k == null) return;
                if (on) k.SetValue(RegistryValueName, "\"" + ExePath + "\"");
                else k.DeleteValue(RegistryValueName, false);
                k.Close();
                Logger.Write("INFO", "استارتآپ رجیستری (HKCU Run) " + (on ? "ثبت" : "حذف") + " شد.");
            }
            catch (Exception ex)
            {
                Logger.Write("WARN", "خطای رجیستری: " + ex.Message);
            }
        }

        // حذف آثار نسخه قدیمی PowerShell (v1) در صورت ارتقا به نسخه C#
        public static void CleanupLegacy()
        {
            try
            {
                RunCmd("schtasks", "/Delete /TN NetworkWatchdog_AutoStart /F");
            }
            catch { }
        }

        static int RunCmd(string exe, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(30000);
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Logger.Write("WARN", "اجرای " + exe + " ناموفق: " + ex.Message);
                return -1;
            }
        }
    }

    // ========================================================================
    //  ساخت آیکون‌های رنگی System Tray در زمان اجرا (بدون نیاز به فایل آیکون)
    // ========================================================================
    internal static class IconFactory
    {
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr hIcon);

        public static Icon Make(Color back)
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (SolidBrush b = new SolidBrush(back))
                    {
                        g.FillEllipse(b, 1, 1, 30, 30);
                    }
                    using (SolidBrush w = new SolidBrush(Color.White))
                    {
                        g.FillRectangle(w, 7, 17, 5, 7);    // میله کوچک
                        g.FillRectangle(w, 14, 12, 5, 12);  // میله متوسط
                        g.FillRectangle(w, 21, 7, 5, 17);   // میله بزرگ
                    }
                }
                IntPtr h = bmp.GetHicon();
                try
                {
                    Icon tmp = Icon.FromHandle(h);
                    Icon result = (Icon)tmp.Clone();
                    return result;
                }
                finally
                {
                    DestroyIcon(h);
                }
            }
        }
    }

    // ========================================================================
    //  فرم تنظیمات (فارسی - راست به چپ)
    // ========================================================================
    public class SettingsForm : Form
    {
        readonly TrayContext _owner;
        readonly AppSettings _edit;

        TextBox txtInet;
        TextBox txtLocal;
        TextBox txtExclude;
        NumericUpDown numInterval;
        NumericUpDown numTimeout;
        NumericUpDown numDelay;
        NumericUpDown numUpWait;
        NumericUpDown numRetries;
        CheckBox chkProtect;
        CheckBox chkBalloons;
        CheckBox chkStartup;

        // آداپتورهای انتخابی (v2.3)
        CheckedListBox clbAdapters;
        Button btnAdaptRefresh;
        Button btnAdaptAll;
        Button btnAdaptNone;
        Label lblAdapterStatus;
        List<AdapterManager.AdapterInfo> _adapterInfos = new List<AdapterManager.AdapterInfo>();
        bool _adaptersLoaded;

        public SettingsForm(TrayContext owner, AppSettings current)
        {
            _owner = owner;
            _edit = current.Clone();
            BuildUi();
            // فهرست آداپتورها پس از نمایش فرم در پس‌زمینه خوانده می‌شود
            this.Shown += new EventHandler(OnFirstShown);
        }

        void OnFirstShown(object sender, EventArgs e)
        {
            LoadAdapters();
        }

        void BuildUi()
        {
            this.Text = "تنظیمات Network Watchdog";
            this.Font = new Font("Segoe UI", 9F);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(476, 756);
            this.RightToLeft = RightToLeft.Yes;
            this.RightToLeftLayout = true;

            // ---------- گروه: بررسی اتصال ----------
            GroupBox grpCheck = new GroupBox();
            grpCheck.Text = "بررسی اتصال";
            grpCheck.SetBounds(12, 10, 452, 200);
            grpCheck.RightToLeft = RightToLeft.Yes;

            Label lblInet = new Label();
            lblInet.Text = "آدرس‌های بررسی اینترنت (با کاما جدا کنید):";
            lblInet.AutoSize = true;
            lblInet.SetBounds(10, 24, 430, 17);
            txtInet = new TextBox();
            txtInet.SetBounds(10, 44, 432, 23);
            txtInet.Text = _edit.InternetTargets;

            Label lblLocal = new Label();
            lblLocal.Text = "آدرس‌های شبکه محلی / دوربین‌ها (مثلاً 192.168.1.1):";
            lblLocal.AutoSize = true;
            lblLocal.SetBounds(10, 74, 430, 17);
            txtLocal = new TextBox();
            txtLocal.SetBounds(10, 94, 432, 23);
            txtLocal.Text = _edit.LocalTargets;

            chkProtect = new CheckBox();
            chkProtect.Text = "اگر شبکه محلی (دوربین‌ها) در دسترس بود، شبکه هرگز ری‌استارت نشود";
            chkProtect.AutoSize = true;
            chkProtect.SetBounds(10, 124, 432, 22);
            chkProtect.Checked = _edit.ProtectLocalNetwork;

            Label lblInterval = new Label();
            lblInterval.Text = "فاصله هر بررسی (ثانیه):";
            lblInterval.AutoSize = true;
            lblInterval.SetBounds(10, 156, 130, 17);
            numInterval = new NumericUpDown();
            numInterval.SetBounds(345, 152, 97, 23);
            numInterval.Minimum = 10;
            numInterval.Maximum = 86400;
            numInterval.Value = Math.Min(Math.Max(_edit.CheckIntervalSeconds, 10), 86400);

            grpCheck.Controls.Add(lblInet);
            grpCheck.Controls.Add(txtInet);
            grpCheck.Controls.Add(lblLocal);
            grpCheck.Controls.Add(txtLocal);
            grpCheck.Controls.Add(chkProtect);
            grpCheck.Controls.Add(lblInterval);
            grpCheck.Controls.Add(numInterval);

            // ---------- گروه: ری‌استارت شبکه ----------
            GroupBox grpRestart = new GroupBox();
            grpRestart.Text = "ری‌استارت شبکه";
            grpRestart.SetBounds(12, 216, 452, 212);
            grpRestart.RightToLeft = RightToLeft.Yes;

            Label lblDelay = new Label();
            lblDelay.Text = "تاخیر بین غیرفعال و فعال‌سازی آداپتور (ثانیه):";
            lblDelay.AutoSize = true;
            lblDelay.SetBounds(10, 24, 280, 17);
            numDelay = new NumericUpDown();
            numDelay.SetBounds(345, 20, 97, 23);
            numDelay.Minimum = 1;
            numDelay.Maximum = 120;
            numDelay.Value = Math.Min(Math.Max(_edit.RestartDelaySeconds, 1), 120);

            Label lblUpWait = new Label();
            lblUpWait.Text = "صبر برای فعال شدن آداپتور (ثانیه):";
            lblUpWait.AutoSize = true;
            lblUpWait.SetBounds(10, 56, 280, 17);
            numUpWait = new NumericUpDown();
            numUpWait.SetBounds(345, 52, 97, 23);
            numUpWait.Minimum = 1;
            numUpWait.Maximum = 300;
            numUpWait.Value = Math.Min(Math.Max(_edit.AdapterUpWaitSeconds, 1), 300);

            Label lblRetries = new Label();
            lblRetries.Text = "تعداد تلاش مجدد برای هر آداپتور:";
            lblRetries.AutoSize = true;
            lblRetries.SetBounds(10, 88, 280, 17);
            numRetries = new NumericUpDown();
            numRetries.SetBounds(345, 84, 97, 23);
            numRetries.Minimum = 1;
            numRetries.Maximum = 10;
            numRetries.Value = Math.Min(Math.Max(_edit.MaxRestartRetries, 1), 10);

            Label lblTimeout = new Label();
            lblTimeout.Text = "مهلت هر تست اتصال - Timeout (میلی‌ثانیه):";
            lblTimeout.AutoSize = true;
            lblTimeout.SetBounds(10, 120, 280, 17);
            numTimeout = new NumericUpDown();
            numTimeout.SetBounds(345, 116, 97, 23);
            numTimeout.Minimum = 200;
            numTimeout.Maximum = 15000;
            numTimeout.Value = Math.Min(Math.Max(_edit.PingTimeoutMs, 200), 15000);

            Label lblExclude = new Label();
            lblExclude.Text = "الگوی حذف آداپتورهای مجازی (Regex):";
            lblExclude.AutoSize = true;
            lblExclude.SetBounds(10, 152, 280, 17);
            txtExclude = new TextBox();
            txtExclude.SetBounds(10, 172, 432, 23);

            grpRestart.Controls.Add(lblDelay);
            grpRestart.Controls.Add(numDelay);
            grpRestart.Controls.Add(lblUpWait);
            grpRestart.Controls.Add(numUpWait);
            grpRestart.Controls.Add(lblRetries);
            grpRestart.Controls.Add(numRetries);
            grpRestart.Controls.Add(lblTimeout);
            grpRestart.Controls.Add(numTimeout);
            grpRestart.Controls.Add(lblExclude);
            grpRestart.Controls.Add(txtExclude);

            // ---------- گروه: آداپتورهای تحت پایش (v2.3) ----------
            GroupBox grpAdapters = new GroupBox();
            grpAdapters.Text = "آداپتورهای تحت پایش و ری‌استارت";
            grpAdapters.SetBounds(12, 434, 452, 192);
            grpAdapters.RightToLeft = RightToLeft.Yes;

            Label lblAdaptersHelp = new Label();
            lblAdaptersHelp.Text = "آداپتورهایی که باید پایش شوند و ری‌استارت روی آن‌ها اعمال شود را علامت بزنید.\n" +
                "اگر هیچ‌کدام انتخاب نشود، همه آداپتورهای غیرمجازی پایش و ری‌استارت می‌شوند.";
            lblAdaptersHelp.AutoSize = false;
            lblAdaptersHelp.SetBounds(10, 22, 432, 32);

            clbAdapters = new CheckedListBox();
            clbAdapters.SetBounds(10, 56, 432, 86);
            clbAdapters.CheckOnClick = true;
            clbAdapters.HorizontalScrollbar = true;
            clbAdapters.RightToLeft = RightToLeft.Yes;

            btnAdaptAll = new Button();
            btnAdaptAll.Text = "انتخاب همه";
            btnAdaptAll.SetBounds(10, 148, 88, 26);
            btnAdaptAll.Click += new EventHandler(OnAdaptSelectAll);

            btnAdaptNone = new Button();
            btnAdaptNone.Text = "انتخاب هیچ‌کدام";
            btnAdaptNone.SetBounds(101, 148, 104, 26);
            btnAdaptNone.Click += new EventHandler(OnAdaptSelectNone);

            btnAdaptRefresh = new Button();
            btnAdaptRefresh.Text = "به‌روزرسانی فهرست";
            btnAdaptRefresh.SetBounds(208, 148, 112, 26);
            btnAdaptRefresh.Click += new EventHandler(OnAdaptRefresh);

            lblAdapterStatus = new Label();
            lblAdapterStatus.Text = "در حال بارگذاری آداپتورها...";
            lblAdapterStatus.AutoSize = false;
            lblAdapterStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblAdapterStatus.SetBounds(322, 150, 120, 22);
            lblAdapterStatus.ForeColor = Color.FromArgb(90, 90, 90);

            grpAdapters.Controls.Add(lblAdaptersHelp);
            grpAdapters.Controls.Add(clbAdapters);
            grpAdapters.Controls.Add(btnAdaptAll);
            grpAdapters.Controls.Add(btnAdaptNone);
            grpAdapters.Controls.Add(btnAdaptRefresh);
            grpAdapters.Controls.Add(lblAdapterStatus);

            // ---------- سایر گزینه‌ها ----------
            chkStartup = new CheckBox();
            chkStartup.Text = "اجرای خودکار هنگام روشن شدن ویندوز (استارتآپ)";
            chkStartup.AutoSize = true;
            chkStartup.SetBounds(12, 634, 452, 22);
            chkStartup.Checked = _edit.RunAtStartup;

            chkBalloons = new CheckBox();
            chkBalloons.Text = "نمایش اعلان‌ها (Balloon Tips) هنگام تغییر وضعیت";
            chkBalloons.AutoSize = true;
            chkBalloons.SetBounds(12, 660, 452, 22);
            chkBalloons.Checked = _edit.ShowBalloons;

            Button btnSave = new Button();
            btnSave.Text = "ذخیره";
            btnSave.SetBounds(364, 690, 100, 32);
            btnSave.Click += new EventHandler(OnSave);

            Button btnCancel = new Button();
            btnCancel.Text = "انصراف";
            btnCancel.SetBounds(256, 690, 100, 32);
            btnCancel.DialogResult = DialogResult.Cancel;

            // ---------- امضا (انتهای برنامه) ----------
            Label lblCredit = new Label();
            lblCredit.Text = "برنامه‌نویسی شده توسط کمالی";
            lblCredit.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            lblCredit.ForeColor = Color.FromArgb(46, 110, 62);
            lblCredit.TextAlign = ContentAlignment.MiddleCenter;
            lblCredit.BackColor = Color.FromArgb(240, 244, 240);
            lblCredit.BorderStyle = BorderStyle.FixedSingle;
            lblCredit.SetBounds(12, 728, 452, 24);

            this.Controls.Add(grpCheck);
            this.Controls.Add(grpRestart);
            this.Controls.Add(grpAdapters);
            this.Controls.Add(chkStartup);
            this.Controls.Add(chkBalloons);
            this.Controls.Add(btnSave);
            this.Controls.Add(btnCancel);
            this.Controls.Add(lblCredit);
            this.CancelButton = btnCancel;
            this.AcceptButton = btnSave;
        }

        void OnSave(object sender, EventArgs e)
        {
            // اعتبارسنجی Regex
            try
            {
                new Regex(txtExclude.Text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch
            {
                MessageBox.Show("الگوی Regex نامعتبر است. لطفاً اصلاح کنید.",
                    "خطا", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            AppSettings ns = _owner.CurrentSettings.Clone();
            ns.InternetTargets = txtInet.Text;
            ns.LocalTargets = txtLocal.Text;
            ns.ProtectLocalNetwork = chkProtect.Checked;
            ns.CheckIntervalSeconds = (int)numInterval.Value;
            ns.RestartDelaySeconds = (int)numDelay.Value;
            ns.AdapterUpWaitSeconds = (int)numUpWait.Value;
            ns.MaxRestartRetries = (int)numRetries.Value;
            ns.PingTimeoutMs = (int)numTimeout.Value;
            ns.AdapterExcludePattern = txtExclude.Text;
            ns.ShowBalloons = chkBalloons.Checked;
            ns.RunAtStartup = chkStartup.Checked;

            // آداپتورهای انتخابی (اگر فهرست هنوز بارگذاری نشده، مقدار قبلی حفظ می‌شود)
            if (_adaptersLoaded)
            {
                StringBuilder sbAdapters = new StringBuilder();
                for (int i = 0; i < clbAdapters.Items.Count; i++)
                {
                    if (clbAdapters.GetItemChecked(i))
                    {
                        if (sbAdapters.Length > 0) sbAdapters.Append(", ");
                        sbAdapters.Append(_adapterInfos[i].ConnectionId);
                    }
                }
                ns.MonitoredAdapters = sbAdapters.ToString();
            }

            _owner.ApplySettings(ns);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        // ------------------------------------------------------------------
        //  آداپتورهای انتخابی: بارگذاری پس‌زمینه و دکمه‌ها (v2.3)
        // ------------------------------------------------------------------
        void LoadAdapters()
        {
            if (!IsHandleCreated) return;   // BeginInvoke نیازمند Handle است
            lblAdapterStatus.Text = "در حال خواندن آداپتورها...";
            btnAdaptRefresh.Enabled = false;
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                List<AdapterManager.AdapterInfo> list = null;
                try { list = AdapterManager.GetAllAdapters(_edit.AdapterExcludePattern); }
                catch { }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        PopulateAdapters(list);
                        btnAdaptRefresh.Enabled = true;
                        lblAdapterStatus.Text = list == null
                            ? "خطا در خواندن فهرست"
                            : list.Count + " آداپتور یافت شد";
                    });
                }
                catch { }
            });
        }

        void PopulateAdapters(List<AdapterManager.AdapterInfo> list)
        {
            clbAdapters.BeginUpdate();
            try
            {
                clbAdapters.Items.Clear();
                _adapterInfos.Clear();
                if (list != null)
                {
                    string[] sel = _edit.GetMonitoredAdapters();
                    foreach (AdapterManager.AdapterInfo a in list)
                    {
                        string text = a.ConnectionId + "  |  " +
                            (a.Enabled ? "فعال" : "غیرفعال") +
                            (a.Description.Length > 0 ? "  |  " + a.Description : "");
                        clbAdapters.Items.Add(text, IsAdapterSelected(sel, a.ConnectionId));
                        _adapterInfos.Add(a);
                    }
                }
                _adaptersLoaded = list != null;
            }
            finally
            {
                clbAdapters.EndUpdate();
            }
        }

        static bool IsAdapterSelected(string[] sel, string connectionId)
        {
            if (sel == null) return false;
            foreach (string s in sel)
            {
                if (string.Compare(s, connectionId, StringComparison.OrdinalIgnoreCase) == 0)
                    return true;
            }
            return false;
        }

        void OnAdaptSelectAll(object sender, EventArgs e)
        {
            for (int i = 0; i < clbAdapters.Items.Count; i++)
                clbAdapters.SetItemChecked(i, true);
        }

        void OnAdaptSelectNone(object sender, EventArgs e)
        {
            for (int i = 0; i < clbAdapters.Items.Count; i++)
                clbAdapters.SetItemChecked(i, false);
        }

        void OnAdaptRefresh(object sender, EventArgs e)
        {
            LoadAdapters();
        }
    }

    // ========================================================================
    //  پنجره لاگ زنده - نمایش لحظه‌ای اقدامات، نتایج و دستورات در حال اجرا
    //  هر چیزی که در فایل watchdog.log ثبت می‌شود، همین‌جا به‌صورت زنده
    //  (با رنگ‌بندی سطح پیام) دیده می‌شود؛ علاوه بر آن تاریخچه ابتدای
    //  فایل لاگ هم هنگام باز شدن بارگذاری می‌شود.
    //  نوار وضعیت پایین، «فعالیت جاری» برنامه را نشان می‌دهد.
    // ========================================================================
    public class LogForm : Form
    {
        const int MaxLines = 3000;   // حداکثر خطوط نگهداری‌شده در حافظه
        const int TrimChunk = 500;   // مقدار حذف از ابتدای لیست هنگام سرریز

        readonly object _sync = new object();
        readonly List<string> _all = new List<string>(1024);
        readonly Queue<string> _pending = new Queue<string>();

        RichTextBox _txt;
        ComboBox _cmbLevel;
        CheckBox _chkAutoScroll;
        Button _btnPause;
        Button _btnClear;
        Button _btnCopy;
        Button _btnSave;
        Button _btnOpenFile;
        StatusStrip _status;
        ToolStripStatusLabel _lblActivity;
        ToolStripStatusLabel _lblCount;
        ToolStripStatusLabel _lblFile;
        ToolStripStatusLabel _lblCredit;
        Icon _ownIcon;

        bool _paused;
        bool _needFullRender;
        int _level;   // 0 = همه | 1 = اطلاعات و مهم‌تر | 2 = هشدار و مهم‌تر

        [DllImport("user32.dll")]
        static extern IntPtr SendMessageNative(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        const int WM_SETREDRAW = 11;

        public LogForm()
        {
            BuildUi();
            // مشترک شدن در رخدادهای لاگ و فعالیت (لغو اشتراک در OnFormClosed)
            Logger.LineLogged += OnLineLogged;
            Activity.Changed += OnActivityChanged;
        }

        void BuildUi()
        {
            this.Text = "لاگ زنده - Network Watchdog " + AppInfo.Version;
            this.Font = new Font("Segoe UI", 9F);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(860, 540);
            this.MinimumSize = new Size(840, 380);
            this.RightToLeft = RightToLeft.Yes;
            this.RightToLeftLayout = true;   // چیدمان فارسی (آینه‌ای)

            _ownIcon = IconFactory.Make(Color.FromArgb(46, 160, 67));
            this.Icon = _ownIcon;

            _txt = new RichTextBox();
            _txt.ReadOnly = true;
            _txt.WordWrap = false;
            _txt.DetectUrls = false;
            _txt.Font = new Font("Consolas", 9F);
            _txt.RightToLeft = RightToLeft.No;   // متن لاگ (دستورات) چپ‌به‌راست
            _txt.BorderStyle = BorderStyle.Fixed3D;
            _txt.BackColor = Color.White;

            _cmbLevel = new ComboBox();
            _cmbLevel.DropDownStyle = ComboBoxStyle.DropDownList;
            _cmbLevel.Items.Add("نمایش همه پیام‌ها");
            _cmbLevel.Items.Add("فقط اطلاعات و مهم‌تر");
            _cmbLevel.Items.Add("فقط هشدار و خطاها");
            _cmbLevel.SelectedIndex = 0;
            _cmbLevel.SetBounds(10, 9, 140, 23);
            _cmbLevel.SelectedIndexChanged += new EventHandler(OnLevelChanged);

            _chkAutoScroll = new CheckBox();
            _chkAutoScroll.Text = "اسکرول خودکار";
            _chkAutoScroll.AutoSize = true;
            _chkAutoScroll.SetBounds(158, 11, 105, 22);
            _chkAutoScroll.Checked = true;

            _btnPause = new Button();
            _btnPause.Text = "توقف نمایش";
            _btnPause.SetBounds(268, 7, 95, 28);
            _btnPause.Click += new EventHandler(OnPauseToggle);

            _btnCopy = new Button();
            _btnCopy.Text = "کپی همه";
            _btnCopy.SetBounds(370, 7, 75, 28);
            _btnCopy.Click += new EventHandler(OnCopyAll);

            _btnSave = new Button();
            _btnSave.Text = "ذخیره در فایل";
            _btnSave.SetBounds(452, 7, 100, 28);
            _btnSave.Click += new EventHandler(OnSaveToFile);

            _btnClear = new Button();
            _btnClear.Text = "پاک کردن نمایش";
            _btnClear.SetBounds(559, 7, 110, 28);
            _btnClear.Click += new EventHandler(OnClearView);

            _btnOpenFile = new Button();
            _btnOpenFile.Text = "باز کردن فایل لاگ";
            _btnOpenFile.SetBounds(676, 7, 120, 28);
            _btnOpenFile.Click += new EventHandler(OnOpenLogFile);

            _lblActivity = new ToolStripStatusLabel();
            _lblActivity.Spring = true;
            _lblActivity.Text = "فعالیت فعلی: -";
            _lblActivity.TextAlign = ContentAlignment.MiddleRight;

            _lblCount = new ToolStripStatusLabel();
            _lblCount.Text = "0 خط";

            _lblFile = new ToolStripStatusLabel();
            _lblFile.Text = "فایل: " + Path.GetFileName(Logger.FilePath);

            _lblCredit = new ToolStripStatusLabel();
            _lblCredit.Text = "برنامه‌نویسی شده توسط کمالی";
            _lblCredit.ForeColor = Color.FromArgb(46, 110, 62);
            _lblCredit.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);

            _status = new StatusStrip();
            _status.Items.Add(_lblActivity);
            _status.Items.Add(_lblCount);
            _status.Items.Add(_lblFile);
            _status.Items.Add(_lblCredit);

            this.Controls.Add(_txt);
            this.Controls.Add(_status);
            this.Controls.Add(_cmbLevel);
            this.Controls.Add(_chkAutoScroll);
            this.Controls.Add(_btnPause);
            this.Controls.Add(_btnCopy);
            this.Controls.Add(_btnSave);
            this.Controls.Add(_btnClear);
            this.Controls.Add(_btnOpenFile);
            LayoutManual();
        }

        void LayoutManual()
        {
            int w = this.ClientSize.Width;
            int h = this.ClientSize.Height;
            int statusH = 28;
            _txt.SetBounds(0, 44, w, Math.Max(80, h - 44 - statusH));
            _status.SetBounds(0, h - statusH, w, statusH);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            try { LayoutManual(); }
            catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LoadTail();                       // تاریخچه ابتدای فایل لاگ
            FlushNow();                       // خطوط در صف مانده (قبل از باز شدن پنجره)
            UpdateActivityText(Activity.Current);
        }

        // ------------------------------------------------------------------
        //  دریافت خطوط جدید (از هر تردی) و انتقال امن به رابط کاربری
        // ------------------------------------------------------------------
        void OnLineLogged(string line)
        {
            lock (_sync) { _pending.Enqueue(line); }
            QueueFlush();
        }

        void OnActivityChanged(string text)
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((MethodInvoker)delegate { UpdateActivityText(text); });
            }
            catch { }
        }

        void UpdateActivityText(string text)
        {
            _lblActivity.Text = "فعالیت فعلی: " + (string.IsNullOrEmpty(text) ? "-" : text);
        }

        void QueueFlush()
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke((MethodInvoker)delegate { FlushNow(); });
            }
            catch { }
        }

        void FlushNow()
        {
            if (IsDisposed) return;
            Queue<string> batch = null;
            lock (_sync)
            {
                if (_pending.Count > 0)
                {
                    batch = new Queue<string>(_pending);
                    _pending.Clear();
                }
            }
            if (batch != null)
            {
                foreach (string line in batch) _all.Add(line);
                while (_all.Count > MaxLines)
                {
                    _all.RemoveRange(0, Math.Min(TrimChunk, _all.Count));
                    _needFullRender = true;
                }
            }
            if (_needFullRender)
            {
                RenderAll();
                return;
            }
            if (batch != null && !_paused)
            {
                foreach (string line in batch)
                {
                    if (MatchesFilter(line)) AppendColored(line);
                }
                if (_chkAutoScroll.Checked) ScrollEnd();
            }
            UpdateCount();
        }

        // بازسازی کامل متن نمایش (تغییر فیلتر / رفع توقف / سرریز حافظه)
        void RenderAll()
        {
            _needFullRender = false;
            try
            {
                SendMessageNative(_txt.Handle, WM_SETREDRAW, (IntPtr)0, IntPtr.Zero);
                _txt.Clear();
                foreach (string line in _all)
                {
                    if (MatchesFilter(line)) AppendColored(line);
                }
                SendMessageNative(_txt.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero);
                _txt.Invalidate();
            }
            catch
            {
                try { SendMessageNative(_txt.Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero); }
                catch { }
            }
            if (_chkAutoScroll.Checked) ScrollEnd();
            UpdateCount();
        }

        void AppendColored(string line)
        {
            _txt.SelectionStart = _txt.TextLength;
            _txt.SelectionLength = 0;
            _txt.SelectionColor = ColorForLine(line);
            _txt.AppendText(line + "\n");
        }

        static int LevelOf(string line)
        {
            if (line.Contains("[DEBUG]")) return 0;
            if (line.Contains("[INFO]")) return 1;
            if (line.Contains("[WARN]")) return 2;
            if (line.Contains("[ERROR]")) return 3;
            return 1;
        }

        bool MatchesFilter(string line)
        {
            if (_level == 0) return true;
            return LevelOf(line) >= _level;
        }

        static Color ColorForLine(string line)
        {
            int lv = LevelOf(line);
            if (lv == 3) return Color.FromArgb(196, 43, 28);     // خطا: قرمز
            if (lv == 2) return Color.FromArgb(156, 106, 12);    // هشدار: کهربایی
            if (lv == 0) return Color.FromArgb(120, 120, 120);   // ریزه‌کاری: خاکستری
            return Color.FromArgb(32, 32, 32);                   // اطلاعات: مشکی
        }

        void ScrollEnd()
        {
            try
            {
                _txt.SelectionStart = _txt.TextLength;
                _txt.SelectionLength = 0;
                _txt.ScrollToCaret();
            }
            catch { }
        }

        void UpdateCount()
        {
            _lblCount.Text = _all.Count + " خط";
        }

        // خواندن انتهای فایل لاگ برای نمایش تاریخچه قبل از باز شدن پنجره
        void LoadTail()
        {
            try
            {
                if (!File.Exists(Logger.FilePath)) return;
                using (FileStream fs = new FileStream(Logger.FilePath,
                    FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length > 200 * 1024)
                        fs.Seek(-200 * 1024, SeekOrigin.End);
                    else
                        fs.Seek(0, SeekOrigin.Begin);
                    using (StreamReader sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        string tail = sr.ReadToEnd();
                        string[] lines = tail.Replace("\r\n", "\n").Split('\n');
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string t = lines[i].TrimEnd();
                            if (t.Length == 0) continue;
                            if (i == 0 && !t.StartsWith("[")) continue;   // نیمه‌خط ابتدای فایل
                            _all.Add(t);
                        }
                    }
                }
                while (_all.Count > MaxLines) _all.RemoveRange(0, TrimChunk);
            }
            catch { }
            RenderAll();
        }

        // ------------------------------------------------------------------
        //  رویدادهای دکمه‌ها
        // ------------------------------------------------------------------
        void OnPauseToggle(object sender, EventArgs e)
        {
            _paused = !_paused;
            _btnPause.Text = _paused ? "ادامه نمایش" : "توقف نمایش";
            if (!_paused) RenderAll();
        }

        void OnLevelChanged(object sender, EventArgs e)
        {
            _level = _cmbLevel.SelectedIndex;
            if (_level < 0) _level = 0;
            if (IsHandleCreated) RenderAll();   // قبل از ساخته شدن پنجره نیازی به بازسازی نیست
        }

        void OnClearView(object sender, EventArgs e)
        {
            _txt.Clear();
            lock (_sync) { _pending.Clear(); }
            _all.Clear();
            UpdateCount();
        }

        void OnCopyAll(object sender, EventArgs e)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (string line in _all)
                {
                    if (MatchesFilter(line)) sb.AppendLine(line);
                }
                if (sb.Length == 0) return;
                Clipboard.SetText(sb.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطا در کپی: " + ex.Message, "خطا",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void OnSaveToFile(object sender, EventArgs e)
        {
            try
            {
                using (SaveFileDialog dlg = new SaveFileDialog())
                {
                    dlg.Title = "ذخیره لاگ";
                    dlg.FileName = "watchdog-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
                    dlg.Filter = "فایل متنی (*.txt)|*.txt|همه فایل‌ها (*.*)|*.*";
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
                    StringBuilder sb = new StringBuilder();
                    foreach (string line in _all)
                    {
                        if (MatchesFilter(line)) sb.AppendLine(line);
                    }
                    File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطا در ذخیره: " + ex.Message, "خطا",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void OnOpenLogFile(object sender, EventArgs e)
        {
            try { Process.Start("notepad.exe", Logger.FilePath); }
            catch (Exception ex)
            {
                MessageBox.Show("خطا در باز کردن فایل: " + ex.Message, "خطا",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // لغو اشتراک از رخدادهای استاتیک تا فرم در حافظه نماند
            Logger.LineLogged -= OnLineLogged;
            Activity.Changed -= OnActivityChanged;
            try { if (_ownIcon != null) _ownIcon.Dispose(); }
            catch { }
            base.OnFormClosed(e);
        }
    }

    // ========================================================================
    //  هسته برنامه: آیکون System Tray + حلقه مانیتورینگ
    // ========================================================================
    public class TrayContext : ApplicationContext
    {
        NotifyIcon _tray;
        ContextMenuStrip _menu;
        Form _marshal;
        Thread _worker;
        volatile bool _running;
        volatile AppSettings _settings;
        int _restarting = 0;
        AutoResetEvent _wake = new AutoResetEvent(false);

        Icon _iconOk;
        Icon _iconWarn;
        Icon _iconBad;
        Icon _iconIdle;
        LogForm _logForm;

        bool _lastInet = true;
        bool _lastLocal = true;
        bool _hasStatus = false;
        DateTime _lastCheckTime = DateTime.MinValue;

        ToolStripMenuItem _miStatus;
        ToolStripMenuItem _miCheckNow;
        ToolStripMenuItem _miRestart;
        ToolStripMenuItem _miSettings;
        ToolStripMenuItem _miLiveLog;
        ToolStripMenuItem _miViewLog;
        ToolStripMenuItem _miOpenLogDir;
        ToolStripMenuItem _miStartup;
        ToolStripMenuItem _miUninstall;
        ToolStripMenuItem _miAbout;
        ToolStripMenuItem _miExit;

        public AppSettings CurrentSettings
        {
            get { return _settings; }
        }

        public TrayContext()
        {
            _settings = SettingsStore.Load();
            Logger.Configure(_settings.LogMaxSizeKB);

            bool firstRun = !SettingsStore.Exists();
            if (firstRun)
            {
                SettingsStore.Save(_settings);
                Logger.Write("INFO", "اولین اجرا - تنظیمات پیش‌فرض ذخیره شد.");
            }

            Logger.Write("INFO", "========================================================");
            Logger.Write("INFO", "Network Watchdog شروع به کار کرد. نسخه: " + AppInfo.Version);
            Logger.Write("INFO", "نسخه ویندوز: " + Environment.OSVersion.VersionString);
            Logger.Write("INFO", "مسیر برنامه: " + StartupManager.ExePath);
            Logger.Write("INFO", "برنامه‌نویسی شده توسط کمالی");
            Logger.Write("INFO", "========================================================");

            // در اولین اجرا استارتآپ ثبت می‌شود؛ در اجراهای بعدی هم self-heal می‌شود
            try
            {
                if (_settings.RunAtStartup)
                {
                    StartupManager.SetStartup(true);
                }
            }
            catch (Exception ex)
            {
                Logger.Write("WARN", "ثبت استارتآپ ناموفق: " + ex.Message);
            }

            BuildIcons();
            BuildMenu();
            BuildTray();

            if (firstRun)
            {
                ShowBalloon("Network Watchdog نصب شد",
                    "برنامه در پس‌زمینه فعال است و در استارتآپ ویندوز ثبت شد.\n" +
                    "دابل‌کلیک روی آیکون: پنجره لاگ زنده | کلیک راست: تنظیمات و ابزارها",
                    ToolTipIcon.Info);
            }

            Activity.Set("شروع به کار - در حال آماده‌سازی پایش شبکه...");
            _running = true;
            _worker = new Thread(new ThreadStart(Loop));
            _worker.IsBackground = true;
            _worker.Name = "WatchdogLoop";
            _worker.Start();
        }

        // --------------------------------------------------------------------
        void BuildIcons()
        {
            _iconOk = IconFactory.Make(Color.FromArgb(46, 160, 67));    // سبز
            _iconWarn = IconFactory.Make(Color.FromArgb(232, 145, 30)); // نارنجی
            _iconBad = IconFactory.Make(Color.FromArgb(214, 69, 65));   // قرمز
            _iconIdle = IconFactory.Make(Color.FromArgb(130, 130, 130));// خاکستری
        }

        void BuildMenu()
        {
            _menu = new ContextMenuStrip();
            _menu.Font = new Font("Segoe UI", 9F);
            IntPtr forceHandle = _menu.Handle;   // اطمینان از ساخته شدن Handle برای BeginInvoke

            _miStatus = new ToolStripMenuItem();
            _miStatus.Text = "وضعیت: در حال بررسی...";
            _miStatus.Enabled = false;

            _miCheckNow = new ToolStripMenuItem();
            _miCheckNow.Text = "بررسی هم‌اکنون";
            _miCheckNow.Click += new EventHandler(OnCheckNow);

            _miRestart = new ToolStripMenuItem();
            _miRestart.Text = "ری‌استارت شبکه (دستی)";
            _miRestart.Click += new EventHandler(OnManualRestart);

            _miSettings = new ToolStripMenuItem();
            _miSettings.Text = "تنظیمات...";
            _miSettings.Click += new EventHandler(OnSettings);

            _miLiveLog = new ToolStripMenuItem();
            _miLiveLog.Text = "پنجره لاگ زنده (فعالیت‌ها و دستورات)";
            _miLiveLog.Click += new EventHandler(OnLiveLog);

            _miViewLog = new ToolStripMenuItem();
            _miViewLog.Text = "مشاهده فایل لاگ";
            _miViewLog.Click += new EventHandler(OnViewLog);

            _miOpenLogDir = new ToolStripMenuItem();
            _miOpenLogDir.Text = "باز کردن پوشه برنامه";
            _miOpenLogDir.Click += new EventHandler(OnOpenLogDir);

            _miStartup = new ToolStripMenuItem();
            _miStartup.Text = "اجرای خودکار هنگام روشن شدن ویندوز";
            _miStartup.CheckOnClick = true;
            _miStartup.Checked = _settings.RunAtStartup && StartupManager.IsStartupEnabled();
            _miStartup.Click += new EventHandler(OnToggleStartup);

            _miUninstall = new ToolStripMenuItem();
            _miUninstall.Text = "حذف کامل برنامه (Uninstall)";
            _miUninstall.ForeColor = Color.FromArgb(170, 40, 40);
            _miUninstall.Click += new EventHandler(OnUninstall);

            _miAbout = new ToolStripMenuItem();
            _miAbout.Text = "درباره برنامه";
            _miAbout.Click += new EventHandler(OnAbout);

            _miExit = new ToolStripMenuItem();
            _miExit.Text = "خروج";
            _miExit.Click += new EventHandler(OnExit);

            _menu.Items.Add(_miStatus);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miCheckNow);
            _menu.Items.Add(_miRestart);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miSettings);
            _menu.Items.Add(_miLiveLog);
            _menu.Items.Add(_miViewLog);
            _menu.Items.Add(_miOpenLogDir);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miStartup);
            _menu.Items.Add(_miUninstall);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miAbout);
            _menu.Items.Add(_miExit);
        }

        void BuildTray()
        {
            _marshal = new Form();
            _marshal.ShowInTaskbar = false;
            _marshal.WindowState = FormWindowState.Minimized;
            _marshal.Opacity = 0;
            IntPtr h = _marshal.Handle;   // force handle creation (برای Invoke بین Thread ها)

            _tray = new NotifyIcon();
            _tray.Icon = _iconIdle;
            _tray.Text = "Network Watchdog - در حال بررسی...";
            _tray.ContextMenuStrip = _menu;
            _tray.Visible = true;
            _tray.DoubleClick += new EventHandler(OnLiveLog);   // دوبار کلیک ← لاگ زنده
        }

        // --------------------------------------------------------------------
        //  حلقه اصلی مانیتورینگ
        // --------------------------------------------------------------------
        void Loop()
        {
            Thread.Sleep(2000);   // صبر کوتاه برای راه‌اندازی سیستم در بوت
            while (_running)
            {
                try
                {
                    DoCycle();
                }
                catch (Exception ex)
                {
                    Logger.Write("ERROR", "خطای غیرمنتظره در حلقه اصلی: " + ex);
                }
                // انتظار قابل وقفه‌سازی تا دفعه بعد
                AppSettings s = _settings;
                int totalMs = Math.Max(10, s.CheckIntervalSeconds) * 1000;
                Activity.Set("پایش فعال - چرخه بعدی حداکثر تا " + (totalMs / 1000) +
                    " ثانیه دیگر (در صورت قطع، فوراً بررسی می‌شود)");
                int slept = 0;
                while (_running && slept < totalMs)
                {
                    if (_wake.WaitOne(250))
                    {
                        break;   // درخواست بررسی فوری
                    }
                    slept += 250;
                }
            }
        }

        void DoCycle()
        {
            AppSettings s = _settings;
            _lastCheckTime = DateTime.Now;

            string[] inetTargets = s.GetInternetTargets();
            string[] localTargets = s.GetLocalTargets();

            Activity.Set("در حال بررسی اتصال اینترنت و شبکه محلی...");
            Logger.Write("INFO", "==== آغاز چرخه بررسی ====");
            Logger.Write("DEBUG", "آدرس‌های تست اینترنت: " + s.InternetTargets);
            Logger.Write("DEBUG", "آدرس‌های تست شبکه محلی: " + s.LocalTargets);

            bool inet = NetCheck.AnyReachable(inetTargets, s.PingTimeoutMs, s.TcpFallbackPort);
            bool local = NetCheck.AnyReachable(localTargets, s.PingTimeoutMs, 0);
            Logger.Write("INFO", "نتیجه بررسی: اینترنت=" + (inet ? "متصل" : "قطع") +
                " | شبکه محلی=" + (local ? "متصل" : "قطع"));

            UpdateStatus(inet, local, s);

            // نمایش وضعیت آداپتورهای انتخابی کاربر در لاگ (پایش شفاف - v2.3)
            string[] monitoredAdapters = s.GetMonitoredAdapters();
            if (monitoredAdapters.Length > 0)
                AdapterManager.LogSelectedStatus(monitoredAdapters);

            // ---- حالت 1: اینترنت سالم ----
            if (inet)
            {
                if (!local)
                {
                    Logger.Write("WARN",
                        "اینترنت متصل است اما شبکه محلی (دوربین‌ها) در دسترس نیست! " +
                        "برای حفظ اینترنت، ری‌استارت خودکار انجام نمی‌شود.");
                }
                return;
            }

            // ---- حالت 2: اینترنت قطع ولی شبکه محلی (دوربین‌ها) سالم ----
            if (s.ProtectLocalNetwork && local)
            {
                Logger.Write("WARN",
                    "اینترنت قطع است اما شبکه محلی (دوربین‌ها) در دسترس است - " +
                    "بر اساس تنظیمات، ری‌استارت شبکه انجام نشد.");
                return;
            }

            // ---- حالت 3: قطع کامل (اینترنت + شبکه محلی) => ری‌استارت ----
            Logger.Write("WARN", "قطع کامل شبکه شناسایی شد - شروع ری‌استارت آداپتورها...");
            RestartAllAdapters(s);

            // بررسی مجدد پس از ری‌استارت
            Activity.Set("بررسی مجدد اتصال پس از ری‌استارت...");
            Logger.Write("INFO", "انتظار " + Math.Max(1, s.AdapterUpWaitSeconds) +
                " ثانیه برای پایدارسازی شبکه...");
            Thread.Sleep(Math.Max(1, s.AdapterUpWaitSeconds) * 1000);
            bool inet2 = NetCheck.AnyReachable(inetTargets, s.PingTimeoutMs, s.TcpFallbackPort);
            bool local2 = NetCheck.AnyReachable(localTargets, s.PingTimeoutMs, 0);
            Logger.Write("INFO", "نتیجه بررسی پس از ری‌استارت: اینترنت=" + (inet2 ? "متصل" : "قطع") +
                " | شبکه محلی=" + (local2 ? "متصل" : "قطع"));
            UpdateStatus(inet2, local2, s);

            if (inet2)
                Logger.Write("INFO", "اتصال اینترنت پس از ری‌استارت شبکه بازیابی شد.");
            else if (local2)
                Logger.Write("INFO", "شبکه محلی پس از ری‌استارت بازیابی شد.");
            else
                Logger.Write("WARN", "اتصال هنوز برقرار نیست؛ در چرخه بعدی دوباره تلاش می‌شود.");
        }

        // --------------------------------------------------------------------
        //  ری‌استارت آداپتورها (با قفل ضدتداخل بین اجرای دستی و خودکار)
        //  همه مراحل، دستورات و نتایج به‌صورت تفصیلی لاگ می‌شوند
        // --------------------------------------------------------------------
        public bool RestartAllAdapters(AppSettings s)
        {
            if (Interlocked.CompareExchange(ref _restarting, 1, 0) != 0)
            {
                Logger.Write("DEBUG", "ری‌استارت دیگری در حال اجراست - صرف‌نظر شد.");
                return false;
            }
            try
            {
                Activity.Set("ری‌استارت آداپتورهای شبکه در حال اجراست...");
                Logger.Write("INFO", "================= شروع فرآیند ری‌استارت شبکه =================");

                if (s.ShowBalloons)
                {
                    try
                    {
                        _menu.BeginInvoke((MethodInvoker)delegate
                        {
                            ShowBalloon("شروع ری‌استارت شبکه",
                                "ری‌استارت آداپتورهای شبکه آغاز شد.\n" +
                                "برای مشاهده لحظه‌ای جزئیات، پنجره لاگ زنده را باز کنید.",
                                ToolTipIcon.Warning);
                        });
                    }
                    catch { }
                }

                Logger.Write("INFO", "مرحله 1) جستجوی آداپتورهای شبکه (WMI + netsh)...");
                string[] monitored = s.GetMonitoredAdapters();
                if (monitored.Length > 0)
                    Logger.Write("INFO", "انتخاب کاربر فعال است - ری‌استارت فقط روی این آداپتورها اعمال می‌شود: " +
                        s.MonitoredAdapters);
                else
                    Logger.Write("INFO", "انتخاب خاصی ثبت نشده - همه آداپتورهای غیرمجازی هدف هستند.");
                List<AdapterManager.AdapterInfo> adapters =
                    AdapterManager.GetResetTargets(s.AdapterExcludePattern, monitored);
                if (adapters.Count == 0)
                {
                    Logger.Write("WARN", "هیچ آداپتور شبکه مناسب یافت نشد! " +
                        "(آداپتورهای مجازی با الگوی «" + s.AdapterExcludePattern + "» حذف شدند)");
                    return false;
                }

                Logger.Write("INFO", "مرحله 2) " + adapters.Count + " آداپتور برای ری‌استارت مشخص شد:");
                for (int i = 0; i < adapters.Count; i++)
                {
                    AdapterManager.AdapterInfo a = adapters[i];
                    string state = a.Enabled
                        ? "فعال"
                        : (a.DeviceId.Length == 0 ? "وضعیت نامشخص" : "غیرفعال (دوباره فعال می‌شود)");
                    Logger.Write("INFO", "   " + (i + 1) + ". «" + a.ConnectionId + "»" +
                        (a.Description.Length > 0 ? " [" + a.Description + "]" : "") +
                        " - " + state);
                }

                int okCount = 0;
                for (int i = 0; i < adapters.Count; i++)
                {
                    AdapterManager.AdapterInfo a = adapters[i];
                    Activity.Set("ری‌استارت آداپتور " + (i + 1) + " از " + adapters.Count +
                        ": «" + a.ConnectionId + "»");
                    Logger.Write("INFO", "--------- آداپتور " + (i + 1) + " از " + adapters.Count +
                        ": «" + a.ConnectionId + "» ---------");
                    int retries = Math.Max(1, s.MaxRestartRetries);
                    bool success = false;
                    for (int attempt = 1; attempt <= retries && !success; attempt++)
                    {
                        if (attempt > 1)
                        {
                            Logger.Write("INFO", "تلاش مجدد " + attempt + " از " + retries +
                                " برای «" + a.ConnectionId + "»...");
                            Thread.Sleep(2000);
                        }
                        success = AdapterManager.RestartAdapter(a, s);
                    }
                    if (success) okCount++;
                    if (i < adapters.Count - 1)
                    {
                        Logger.Write("INFO", "انتظار " + Math.Max(1, s.AdapterUpWaitSeconds) +
                            " ثانیه پیش از آداپتور بعدی...");
                        Thread.Sleep(Math.Max(1, s.AdapterUpWaitSeconds) * 1000);
                    }
                }

                Logger.Write("INFO", "================= پایان ری‌استارت: " + okCount + " از " +
                    adapters.Count + " آداپتور با موفقیت انجام شد =================");
                return okCount > 0;
            }
            finally
            {
                Interlocked.Exchange(ref _restarting, 0);
            }
        }

        // --------------------------------------------------------------------
        //  به‌روزرسانی وضعیت روی UI (آیکون، متن، اعلان‌ها)
        // --------------------------------------------------------------------
        void UpdateStatus(bool inet, bool local, AppSettings s)
        {
            try
            {
                _menu.BeginInvoke((MethodInvoker)delegate
                {
                    try { UpdateStatusUi(inet, local, s); }
                    catch { }
                });
            }
            catch { }
        }

        void UpdateStatusUi(bool inet, bool local, AppSettings s)
        {
            // اعلان تغییر وضعیت
            if (_hasStatus && s.ShowBalloons)
            {
                if (_lastInet && !inet)
                    ShowBalloon("قطع اتصال اینترنت",
                        local && s.ProtectLocalNetwork
                            ? "اینترنت قطع شد، اما شبکه محلی سالم است. ری‌استارت انجام نمی‌شود."
                            : "اتصال اینترنت قطع شد!",
                        ToolTipIcon.Warning);
                if (!_lastInet && inet)
                    ShowBalloon("بازیابی اینترنت", "اتصال اینترنت مجدداً برقرار شد.", ToolTipIcon.Info);
                if (_lastLocal && !local)
                    ShowBalloon("قطع شبکه محلی", "ارتباط با شبکه محلی (دوربین‌ها) قطع شد!", ToolTipIcon.Error);
                if (!_lastLocal && local)
                    ShowBalloon("بازیابی شبکه محلی", "ارتباط با شبکه محلی برقرار شد.", ToolTipIcon.Info);
            }
            _lastInet = inet;
            _lastLocal = local;
            _hasStatus = true;

            // آیکون: سبز = همه‌چیز سالم | نارنجی = یکی از دو اتصال قطع | قرمز = قطع کامل
            if (inet && local)
                _tray.Icon = _iconOk;
            else if (!inet && !local)
                _tray.Icon = _iconBad;
            else
                _tray.Icon = _iconWarn;

            string inetTxt = inet ? "متصل" : "قطع";
            string localTxt = local ? "متصل" : "قطع";
            string status = "اینترنت: " + inetTxt + "  |  دوربین‌ها: " + localTxt;
            _miStatus.Text = status +
                (_lastCheckTime != DateTime.MinValue
                    ? "   (" + _lastCheckTime.ToString("HH:mm:ss") + ")"
                    : "");
            _tray.Text = (status.Length > 60 ? status.Substring(0, 60) : status);
        }

        void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            try
            {
                if (_tray != null && _tray.Visible)
                {
                    _tray.BalloonTipTitle = title;
                    _tray.BalloonTipText = text;
                    _tray.BalloonTipIcon = icon;
                    _tray.ShowBalloonTip(3000);
                }
            }
            catch { }
        }

        // --------------------------------------------------------------------
        //  اعمال تنظیمات جدید (از فرم تنظیمات)
        // --------------------------------------------------------------------
        public void ApplySettings(AppSettings ns)
        {
            bool startupChanged = ns.RunAtStartup != _settings.RunAtStartup;
            _settings = ns;
            Logger.Configure(ns.LogMaxSizeKB);
            SettingsStore.Save(ns);
            Logger.Write("INFO", "تنظیمات جدید ذخیره و اعمال شد.");
            if (startupChanged)
            {
                try { StartupManager.SetStartup(ns.RunAtStartup); }
                catch (Exception ex) { Logger.Write("WARN", "تغییر استارتآپ ناموفق: " + ex.Message); }
            }
            _miStartup.Checked = ns.RunAtStartup;
            // بلافاصله یک بررسی با تنظیمات جدید انجام شود
            _wake.Set();
        }

        // --------------------------------------------------------------------
        //  رویدادهای منو
        // --------------------------------------------------------------------
        void OnCheckNow(object sender, EventArgs e)
        {
            _wake.Set();
        }

        void OnManualRestart(object sender, EventArgs e)
        {
            DialogResult r = MessageBox.Show(
                "آیا از ری‌استارت دستی شبکه مطمئن هستید؟\n" +
                "تمام اتصال‌های شبکه (از جمله دوربین‌ها) به‌طور موقت قطع می‌شوند.",
                "تایید ری‌استارت", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            ShowLogForm();   // برای دیدن لحظه‌ای مراحل، پنجره لاگ زنده باز می‌شود
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    AppSettings s = _settings;
                    Logger.Write("INFO", "ری‌استارت دستی شبکه توسط کاربر...");
                    RestartAllAdapters(s);
                }
                catch (Exception ex)
                {
                    Logger.Write("ERROR", "خطای ری‌استارت دستی: " + ex.Message);
                }
            });
        }

        void OnSettings(object sender, EventArgs e)
        {
            using (SettingsForm f = new SettingsForm(this, _settings))
            {
                f.ShowDialog(_marshal);
            }
        }

        // --------------------------------------------------------------------
        //  پنجره لاگ زنده
        // --------------------------------------------------------------------
        void OnLiveLog(object sender, EventArgs e)
        {
            ShowLogForm();
        }

        // باز کردن / فعال‌سازی پنجره لاگ زنده
        void ShowLogForm()
        {
            try
            {
                if (_logForm == null || _logForm.IsDisposed)
                {
                    _logForm = new LogForm();
                }
                _logForm.Show();
                _logForm.BringToFront();
                _logForm.Activate();
            }
            catch (Exception ex)
            {
                Logger.Write("WARN", "باز کردن پنجره لاگ زنده ناموفق: " + ex.Message);
            }
        }

        void OnViewLog(object sender, EventArgs e)
        {
            try
            {
                Process.Start("notepad.exe", Logger.FilePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show("خطا در باز کردن لاگ: " + ex.Message,
                    "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void OnOpenLogDir(object sender, EventArgs e)
        {
            try
            {
                string dir = Path.GetDirectoryName(Logger.FilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", dir);
            }
            catch { }
        }

        void OnToggleStartup(object sender, EventArgs e)
        {
            bool want = _miStartup.Checked;
            _settings.RunAtStartup = want;
            SettingsStore.Save(_settings);
            try { StartupManager.SetStartup(want); }
            catch (Exception ex) { Logger.Write("WARN", "تغییر استارتآپ ناموفق: " + ex.Message); }
            _miStartup.Checked = want && StartupManager.IsStartupEnabled();
        }

        // --------------------------------------------------------------------
        //  حذف کامل برنامه (Uninstall)
        // --------------------------------------------------------------------
        void OnUninstall(object sender, EventArgs e)
        {
            DialogResult step1 = MessageBox.Show(
                "آیا می‌خواهید Network Watchdog به‌طور کامل حذف شود؟\n\n" +
                "این عملیات:\n" +
                "-  برنامه را از استارتآپ ویندوز حذف می‌کند\n" +
                "-  آثار نسخه قبلی (PowerShell) را هم پاک می‌کند\n" +
                "-  برنامه را می‌بندد\n" +
                "-  فایل NetworkWatchdog.exe را حذف می‌کند",
                "حذف کامل برنامه",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (step1 != DialogResult.Yes) return;

            DialogResult step2 = MessageBox.Show(
                "فایل‌های تنظیمات و لاگ هم حذف شوند؟\n\n" +
                SettingsStore.Dir + "\n" +
                Logger.Dir + "\n\n" +
                "(اگر No را انتخاب کنید، فقط برنامه و استارتآپ حذف می‌شود)",
                "حذف داده‌ها",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (step2 == DialogResult.Cancel) return;
            bool deleteData = (step2 == DialogResult.Yes);

            MessageBox.Show(
                "حذف برنامه آغاز می‌شود.\n" +
                "پس از بسته شدن برنامه، فایل‌ها به‌صورت خودکار در پس‌زمینه پاک می‌شوند.",
                "حذف Network Watchdog",
                MessageBoxButtons.OK, MessageBoxIcon.Information);

            Uninstall(deleteData);
        }

        void Uninstall(bool deleteData)
        {
            try
            {
                Logger.Write("INFO", "شروع حذف کامل برنامه (Uninstall) - نسخه " + AppInfo.Version);

                // 1) حذف از استارتآپ (تسک زمان‌بندی‌شده + رجیستری)
                try { StartupManager.SetStartup(false); }
                catch (Exception ex) { Logger.Write("WARN", "حذف استارتآپ: " + ex.Message); }

                // 2) پاک کردن آثار نسخه قدیمی PowerShell (v1)
                try { StartupManager.CleanupLegacy(); }
                catch { }

                // 3) حذف فایل EXE و داده‌ها پس از بسته شدن برنامه
                //    (فرمان cmd با تاخیر اجرا می‌شود تا فرآیند کامل بسته شود؛
                //     مسیرها مستقیم روی command line یونیکد ارسال می‌شوند تا
                //     مشکلی با کاراکترهای فارسی مسیرها پیش نیاید)
                try
                {
                    string exePath = StartupManager.ExePath;
                    StringBuilder cmd = new StringBuilder();
                    cmd.Append("/c ping 127.0.0.1 -n 8 >nul");
                    cmd.Append(" & del \"").Append(exePath).Append("\"");
                    if (deleteData)
                    {
                        cmd.Append(" & rmdir /s /q \"").Append(SettingsStore.Dir).Append("\"");
                        cmd.Append(" & rmdir /s /q \"").Append(Logger.Dir).Append("\"");
                    }
                    ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", cmd.ToString());
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    using (Process p = Process.Start(psi)) { }
                    Logger.Write("INFO", "اسکریپت حذف پس از خروج برنامه اجرا می‌شود.");
                }
                catch (Exception ex)
                {
                    Logger.Write("ERROR", "ساخت فرمان حذف ناموفق: " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                Logger.Write("ERROR", "خطای حذف برنامه: " + ex.Message);
            }
            finally
            {
                Shutdown();
            }
        }

        // --------------------------------------------------------------------
        //  درباره برنامه
        // --------------------------------------------------------------------
        void OnAbout(object sender, EventArgs e)
        {
            MessageBox.Show(
                "Network Watchdog  v" + AppInfo.Version + "\n\n" +
                "مانیتور و بازیاب خودکار اتصال اینترنت\n" +
                "همراه با محافظت شبکه محلی دوربین‌ها\n\n" +
                "برنامه‌نویسی شده توسط کمالی\n\n" +
                "https://github.com/Tobeseuss/network-adaptor-auto-reset",
                "درباره Network Watchdog",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void OnExit(object sender, EventArgs e)
        {
            Shutdown();
        }

        void Shutdown()
        {
            _running = false;
            _wake.Set();
            try
            {
                if (_worker != null && _worker.IsAlive)
                {
                    // اگر ری‌استارت شبکه در حال انجام است بیشتر صبر کن تا
                    // آداپتور در حالت غیرفعال رها نشود
                    int waitMs = (_restarting == 1) ? 25000 : 4000;
                    _worker.Join(waitMs);
                }
            }
            catch { }
            try
            {
                if (_logForm != null && !_logForm.IsDisposed)
                {
                    _logForm.Close();
                    _logForm.Dispose();
                }
            }
            catch { }
            try
            {
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                }
            }
            catch { }
            Logger.Write("INFO", "Network Watchdog بسته شد.");
            Application.ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
                    if (_menu != null) _menu.Dispose();
                    if (_marshal != null) _marshal.Dispose();
                    if (_logForm != null && !_logForm.IsDisposed) _logForm.Dispose();
                    if (_iconOk != null) _iconOk.Dispose();
                    if (_iconWarn != null) _iconWarn.Dispose();
                    if (_iconBad != null) _iconBad.Dispose();
                    if (_iconIdle != null) _iconIdle.Dispose();
                }
                catch { }
            }
            base.Dispose(disposing);
        }
    }

    // ========================================================================
    //  نقطه ورود برنامه
    // ========================================================================
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // فقط یک نمونه از برنامه در هر Session اجرا شود
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Local\\NetworkWatchdogApp_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "Network Watchdog از قبل در حال اجراست.\n" +
                        "آیکون برنامه را در System Tray (کنار ساعت) ببینید.",
                        "Network Watchdog", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (TrayContext ctx = new TrayContext())
                {
                    Application.Run(ctx);
                }
            }
        }
    }
}
