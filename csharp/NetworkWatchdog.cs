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
//    3) اگر هم اینترنت و هم شبکه محلی قطع بود، آداپتورهای شبکه فیزیکی
//       Disable و سپس Enable (ری‌استارت) می‌شوند
//    4) در اولین اجرا، خود را در استارتآپ ویندوز (Scheduled Task) ثبت می‌کند
//    5) آیکون کنار ساعت: سبز = سالم | نارنجی = فقط اینترنت قطع | قرمز = قطع کامل
//    6) حذف کامل برنامه (Uninstall) از منوی آیکون: پاک کردن استارتآپ،
//       بستن برنامه، حذف EXE و در صورت درخواست، پاک کردن تنظیمات و لاگ
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        public const string Version = "2.1.0";
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

        public static void Write(string level, string msg)
        {
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
                    string line = string.Format("[{0}] [{1}] {2}\r\n",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), level, msg);
                    File.AppendAllText(_path, line, Encoding.UTF8);
                }
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
                    return r.Status == IPStatus.Success;
                }
            }
            catch
            {
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
                    return false;
                }
                try
                {
                    tc.EndConnect(ar);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            catch
            {
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
    //  مدیریت آداپتورهای شبکه با WMI - سازگار با Windows 7/10/11
    //  (cmdlet های PowerShell مثل Get-NetAdapter در ویندوز 7 وجود ندارند،
    //   اما WMI از ویندوز ویستا به بعد یکسان و پایدار است)
    // ========================================================================
    public static class AdapterManager
    {
        public class AdapterInfo
        {
            public string Name;
            public string Description;
            public string ConnectionId;
            public string DeviceId;
        }

        // آداپتورهای فعال و فیزیکی را برمی‌گرداند (مجازی‌ها با Regex حذف می‌شوند)
        public static List<AdapterInfo> GetEnabledPhysicalAdapters(string excludePattern)
        {
            List<AdapterInfo> list = new List<AdapterInfo>();
            Regex rx = null;
            try
            {
                rx = new Regex(excludePattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            catch
            {
                rx = new Regex("Virtual|Loopback|Tunnel", RegexOptions.IgnoreCase);
            }
            using (ManagementObjectSearcher searcher =
                new ManagementObjectSearcher("SELECT * FROM Win32_NetworkAdapter WHERE NetEnabled=TRUE"))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    string name = Convert.ToString(mo["Name"]);
                    string desc = Convert.ToString(mo["Description"]);
                    string connId = Convert.ToString(mo["NetConnectionID"]);
                    string devId = Convert.ToString(mo["DeviceID"]);
                    if (connId == null || connId.Length == 0) continue;      // آداپتور قابل اتصال نیست
                    if (rx.IsMatch(name) || rx.IsMatch(desc)) continue;      // آداپتور مجازی
                    AdapterInfo ai = new AdapterInfo();
                    ai.Name = name;
                    ai.Description = desc;
                    ai.ConnectionId = connId;
                    ai.DeviceId = devId;
                    list.Add(ai);
                }
            }
            return list;
        }

        // ری‌استارت یک آداپتور: WMI با fallback به netsh
        public static bool RestartAdapter(AdapterInfo a, AppSettings s)
        {
            // ---- روش 1: WMI (ترجیحی) ----
            try
            {
                bool okDisable = InvokeWmi(a.DeviceId, "Disable");
                Logger.Write("DEBUG", "  Disable (WMI): " + (okDisable ? "OK" : "NOT-OK") + " - " + a.Name);
                Thread.Sleep(Math.Max(1, s.RestartDelaySeconds) * 1000);
                bool okEnable = InvokeWmi(a.DeviceId, "Enable");
                Logger.Write("DEBUG", "  Enable  (WMI): " + (okEnable ? "OK" : "NOT-OK") + " - " + a.Name);
                if (okEnable) return true;
            }
            catch (Exception ex)
            {
                Logger.Write("WARN", "  WMI ناموفق: " + ex.Message);
            }
            // ---- روش 2: netsh (جایگزین) ----
            try
            {
                RunNetsh(a.ConnectionId, "disable");
                Thread.Sleep(Math.Max(1, s.RestartDelaySeconds) * 1000);
                RunNetsh(a.ConnectionId, "enable");
                Logger.Write("DEBUG", "  netsh fallback اجرا شد - " + a.Name);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Write("WARN", "  netsh ناموفق: " + ex.Message);
                return false;
            }
        }

        static bool InvokeWmi(string deviceId, string method)
        {
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_NetworkAdapter WHERE DeviceID='" + deviceId + "'"))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    object result = mo.InvokeMethod(method, null);
                    if (result == null) return true;
                    return Convert.ToUInt32(result) == 0;
                }
            }
            return false;
        }

        static void RunNetsh(string connectionId, string action)
        {
            ProcessStartInfo psi = new ProcessStartInfo(
                "netsh", string.Format("interface set interface name=\"{0}\" admin={1}", connectionId, action));
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit(30000);
            }
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

        public SettingsForm(TrayContext owner, AppSettings current)
        {
            _owner = owner;
            _edit = current.Clone();
            BuildUi();
        }

        void BuildUi()
        {
            this.Text = "تنظیمات Network Watchdog";
            this.Font = new Font("Segoe UI", 9F);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(476, 536);
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

            // ---------- سایر گزینه‌ها ----------
            chkStartup = new CheckBox();
            chkStartup.Text = "اجرای خودکار هنگام روشن شدن ویندوز (استارتآپ)";
            chkStartup.AutoSize = true;
            chkStartup.SetBounds(12, 436, 452, 22);
            chkStartup.Checked = _edit.RunAtStartup;

            chkBalloons = new CheckBox();
            chkBalloons.Text = "نمایش اعلان‌ها (Balloon Tips) هنگام تغییر وضعیت";
            chkBalloons.AutoSize = true;
            chkBalloons.SetBounds(12, 462, 452, 22);
            chkBalloons.Checked = _edit.ShowBalloons;

            Button btnSave = new Button();
            btnSave.Text = "ذخیره";
            btnSave.SetBounds(364, 492, 100, 32);
            btnSave.Click += new EventHandler(OnSave);

            Button btnCancel = new Button();
            btnCancel.Text = "انصراف";
            btnCancel.SetBounds(256, 492, 100, 32);
            btnCancel.DialogResult = DialogResult.Cancel;

            this.Controls.Add(grpCheck);
            this.Controls.Add(grpRestart);
            this.Controls.Add(chkStartup);
            this.Controls.Add(chkBalloons);
            this.Controls.Add(btnSave);
            this.Controls.Add(btnCancel);
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

            _owner.ApplySettings(ns);
            this.DialogResult = DialogResult.OK;
            this.Close();
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

        bool _lastInet = true;
        bool _lastLocal = true;
        bool _hasStatus = false;
        DateTime _lastCheckTime = DateTime.MinValue;

        ToolStripMenuItem _miStatus;
        ToolStripMenuItem _miCheckNow;
        ToolStripMenuItem _miRestart;
        ToolStripMenuItem _miSettings;
        ToolStripMenuItem _miViewLog;
        ToolStripMenuItem _miOpenLogDir;
        ToolStripMenuItem _miStartup;
        ToolStripMenuItem _miUninstall;
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
                    "با کلیک راست روی آیکون، تنظیمات را ببینید.",
                    ToolTipIcon.Info);
            }

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

            _miExit = new ToolStripMenuItem();
            _miExit.Text = "خروج";
            _miExit.Click += new EventHandler(OnExit);

            _menu.Items.Add(_miStatus);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miCheckNow);
            _menu.Items.Add(_miRestart);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miSettings);
            _menu.Items.Add(_miViewLog);
            _menu.Items.Add(_miOpenLogDir);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_miStartup);
            _menu.Items.Add(_miUninstall);
            _menu.Items.Add(new ToolStripSeparator());
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
            _tray.DoubleClick += new EventHandler(OnSettings);
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

            bool inet = NetCheck.AnyReachable(inetTargets, s.PingTimeoutMs, s.TcpFallbackPort);
            bool local = NetCheck.AnyReachable(localTargets, s.PingTimeoutMs, 0);
            UpdateStatus(inet, local, s);

            // ---- حالت 1: اینترنت سالم ----
            if (inet)
            {
                if (!local)
                {
                    Logger.Write("WARN",
                        "اینترنت متصل است اما شبکه محلی (دوربین‌ها) در دسترس نیست! " +
                        "برای حفظ اینترنت، ری‌استارت خودکار انجام نمی‌شود.");
                }
                else
                {
                    Logger.Write("DEBUG", "اینترنت: متصل | شبکه محلی: متصل");
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
            Thread.Sleep(Math.Max(1, s.AdapterUpWaitSeconds) * 1000);
            bool inet2 = NetCheck.AnyReachable(inetTargets, s.PingTimeoutMs, s.TcpFallbackPort);
            bool local2 = NetCheck.AnyReachable(localTargets, s.PingTimeoutMs, 0);
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
                List<AdapterManager.AdapterInfo> adapters =
                    AdapterManager.GetEnabledPhysicalAdapters(s.AdapterExcludePattern);
                if (adapters.Count == 0)
                {
                    Logger.Write("WARN", "هیچ آداپتور شبکه فعال و مناسب یافت نشد.");
                    return false;
                }
                Logger.Write("INFO", string.Format(
                    "شروع ری‌استارت {0} آداپتور شبکه...", adapters.Count));
                int ok = 0;
                foreach (AdapterManager.AdapterInfo a in adapters)
                {
                    Logger.Write("INFO", "ری‌استارت: " + a.Name + " (" + a.Description + ")");
                    for (int attempt = 1; attempt <= Math.Max(1, s.MaxRestartRetries); attempt++)
                    {
                        if (AdapterManager.RestartAdapter(a, s))
                        {
                            ok++;
                            break;
                        }
                        Thread.Sleep(2000);
                    }
                    Thread.Sleep(Math.Max(1, s.AdapterUpWaitSeconds) * 1000);
                }
                Logger.Write("INFO", string.Format(
                    "ری‌استارت کامل شد: {0} از {1} آداپتور با موفقیت.", ok, adapters.Count));
                return ok > 0;
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
