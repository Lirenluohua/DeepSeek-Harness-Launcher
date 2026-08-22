// ============================================================================
//  DeepSeek Harness 启动器 (C# WPF 原生版) - 完整重建版
//  功能：启停服务、监控、趋势图、日志、主题、托盘、设置、安全、卸载
//  编译：csc /nologo /target:winexe /codepage:65001 /r:... launcher.cs
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using Path = System.IO.Path;
using WF = System.Windows.Forms;
using SD = System.Drawing;

namespace DSHLauncher
{
    // ---------------- 主题 ----------------
    public static class Theme
    {
        public static bool Light = false;
        private static Dictionary<string, string> Dark = new Dictionary<string, string>()
        {
            {"BgWindow","#151517"},{"BgCard","#1B1B1C"},{"BgCardAlt","#232324"},{"BgLog","#101013"},
            {"FgPrimary","#F9FAFB"},{"FgSecondary","#A1A6AD"},{"FgMuted","#6B7078"},{"FgLog","#C9CDD3"},
            {"BorderW","#22FFFFFF"},{"HoverBg","#1FFFFFFF"},{"PressBg","#2EFFFFFF"},{"ToggleOff","#3A3A3C"},
            {"Accent","#3964FE"},{"Green","#22C55E"},{"Red","#EF4444"},{"Amber","#F7AD31"},{"LinkBlue","#5686FE"},
            {"HiBg","#33F7AD31"}
        };
        private static Dictionary<string, string> LightT = new Dictionary<string, string>()
        {
            {"BgWindow","#F7F8FA"},{"BgCard","#FFFFFF"},{"BgCardAlt","#E9ECF1"},{"BgLog","#F1F3F6"},
            {"FgPrimary","#1C1F24"},{"FgSecondary","#555C66"},{"FgMuted","#8A9199"},{"FgLog","#2B2F36"},
            {"BorderW","#14000000"},{"HoverBg","#14000000"},{"PressBg","#1F000000"},{"ToggleOff","#C9CDD4"},
            {"Accent","#3964FE"},{"Green","#16A34A"},{"Red","#DC2626"},{"Amber","#D97706"},{"LinkBlue","#3B66E4"},
            {"HiBg","#33D97706"}
        };
        public static string Hex(string key)
        {
            Dictionary<string, string> t = Light ? LightT : Dark;
            if (t.ContainsKey(key)) return t[key];
            return "#FFFFFF";
        }
        public static SolidColorBrush Brush(string key) { return new SolidColorBrush(HexColor(key)); }
        public static Color HexColor(string key) { return (Color)ColorConverter.ConvertFromString(Hex(key)); }
        public static string FontFamily = "Segoe UI, Microsoft YaHei, PingFang SC";
        public static bool GetSystemLight()
        {
            try
            {
                RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (k != null)
                {
                    object v = k.GetValue("AppsUseLightTheme");
                    k.Close();
                    if (v != null) return (Convert.ToInt32(v) == 1);
                }
            }
            catch { }
            return false;
        }
    }

    // ---------------- 设置（注册表） ----------------
    public static class Settings
    {
        private const string KeyPath = @"Software\DeepSeekHarness";
        public static string GetString(string name, string def)
        {
            try
            {
                RegistryKey k = Registry.CurrentUser.OpenSubKey(KeyPath);
                if (k != null) { object v = k.GetValue(name); k.Close(); if (v != null) return v.ToString(); }
            }
            catch { }
            return def;
        }
        public static void SetString(string name, string val)
        {
            try
            {
                RegistryKey k = Registry.CurrentUser.CreateSubKey(KeyPath);
                if (k != null) { k.SetValue(name, val); k.Close(); }
            }
            catch { }
        }
        public static int GetInt(string name, int def)
        {
            string s = GetString(name, null);
            int r;
            if (s != null && int.TryParse(s, out r)) return r;
            return def;
        }
        public static void SetInt(string name, int val) { SetString(name, val.ToString()); }
    }

    // ---------------- 主窗体 ----------------
    public class MainForm : Window
    {
        private int Port = Settings.GetInt("port", 3080);
        private bool AutoRestart = (Settings.GetString("autoRestart", "on") != "off");
        private bool AutoOpenWeb = (Settings.GetString("autoOpenWeb", "off") == "on");
        private bool FollowSystem = (Settings.GetString("followSystem", "off") == "on");
        private bool AppModeOpen = (Settings.GetString("appMode", "on") == "on");
        private bool NotifyEnabled = (Settings.GetString("notify", "on") != "off");
        private string ListenAddr = "";
        private DateTime LastAddrCheck = DateTime.MinValue;
        private bool ExposureWarned = false;
        private int CachedPid = -1;
        private DateTime LastPidCheck = DateTime.MinValue;
        private SD.Icon IconColor, IconGray;
        private IntPtr GrayHicon = IntPtr.Zero;
        private string AppDir, LogDir, IcoPath, LauncherLog, ServerOut, ServerErr, ShowFile;
        private string BinPath;
        private bool ReallyExit = false;
        private bool SkipSaveSettings = false;
        private Process ServerProc;
        private WF.NotifyIcon Tray;
        private DispatcherTimer Timer;
        private bool LastState = false;
        private bool Starting = false;
        private DateTime StartingSince = DateTime.MinValue;
        private DateTime LastCpuTime = DateTime.MinValue;
        private TimeSpan LastCpuTick = TimeSpan.Zero;
        private bool HasCpuSample = false;
        private string LastLogText = "";
        private DateTime LogMTime1 = DateTime.MinValue, LogMTime2 = DateTime.MinValue, LogMTime3 = DateTime.MinValue;
        private DateTime LastManualStop = DateTime.MinValue;
        private bool SystemLightTheme = false;
        private int SysCheckCounter = 0;
        private DateTime RunningSince = DateTime.MinValue;
        private List<double> CpuHistory = new List<double>();
        private List<double> MemHistory = new List<double>();
        private double MemMax = 512;
        private List<Action> Repainters = new List<Action>();

        // UI 引用
        private Border BtnToggle, BtnOpen, BtnCopy, BtnClearLog;
        private Grid BtnMin, BtnClose, BtnTheme, BtnSettings;
        private TextBlock ThemeGlyphText;
        private Border TglAutoStart;
        private Action AutoStartApply;
        private Ellipse DotStatus;
        private TextBlock TxtStatus, TxtMeta, TxtUrl, TxtCpu, TxtMem, TxtPort, TxtProc, TxtFooter, TxtWarn;
        private TextBox TxtHighlight;
        private RichTextBox TxtLog;
        private Canvas TrendCanvas;
        private TextBlock TrendLegend, TrendArrow;
        private Border TrendCard;
        private bool TrendCollapsed = (Settings.GetString("trendCollapsed", "off") == "on");
        private bool AutoStartChecked = false;
        private bool UpdateAvailable = false;
        private string UpdateLatestVer = "";
        private Border BtnUpdateBtn;
        private Grid UpdRow;
        private ProgressBar UpdProgMain;
        private TextBlock UpdStatusMain;

        public MainForm()
        {
            try { AppDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); }
            catch { }
            if (String.IsNullOrEmpty(AppDir))
                AppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeepSeekHarness");
            LogDir = Path.Combine(AppDir, "logs");
            IcoPath = Path.Combine(AppDir, "DeepSeek Harness.ico");
            LauncherLog = Path.Combine(LogDir, "launcher.log");
            ServerOut = Path.Combine(LogDir, "server-out.log");
            ServerErr = Path.Combine(LogDir, "server-err.log");
            ShowFile = Path.Combine(AppDir, ".show");
            try { Directory.CreateDirectory(LogDir); } catch { }

            BinPath = FindBin();
            if (BinPath == null) BinPath = Settings.GetString("binpath", null);
            SystemLightTheme = Theme.GetSystemLight();

            BuildUi();
            AutoStartChecked = GetAutoStart();
            ApplyTheme();
            ApplyTrendState();
            RestorePosition();
            BuildTray();
            StartTimer();
            InitFooter();
            Log("Launcher started (C# native, port=" + Port + ")");
            // 启动后自动检查更新（后台，不阻塞）
            Dispatcher.BeginInvoke(new Action(delegate() { AutoCheckUpdate(); }));
        }

        // ---------------- 工具 ----------------
        private void Log(string msg)
        {
            try
            {
                RotateIfLarge(LauncherLog);
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg;
                File.AppendAllText(LauncherLog, line + "\n", Encoding.UTF8);
            }
            catch { }
        }

        // 递归复制目录
        private void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
            {
                string dest = Path.Combine(dst, Path.GetFileName(f));
                File.Copy(f, dest, true);
            }
            foreach (string d in Directory.GetDirectories(src))
            {
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
            }
        }

        private void RotateIfLarge(string path)
        {
            try
            {
                FileInfo fi = new FileInfo(path);
                if (fi.Exists && fi.Length > 1048576)
                {
                    string bak = path + ".old";
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(path, bak);
                }
            }
            catch { }
        }

        private bool PortAlive()
        {
            try
            {
                TcpClient c = new TcpClient();
                IAsyncResult ar = c.BeginConnect("127.0.0.1", Port, null, null);
                bool ok = ar.AsyncWaitHandle.WaitOne(400);
                if (ok) c.EndConnect(ar);
                c.Close();
                return ok;
            }
            catch { return false; }
        }

        private string FindChrome()
        {
            string[] candidates = new string[]
            {
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")
            };
            foreach (string c in candidates)
            {
                try { if (File.Exists(c)) return c; } catch { }
            }
            return null;
        }

        // 检测是否已有该网页的 Chrome 窗口（连接检测 + 标题检测双重）
        private bool AppWindowExists()
        {
            try
            {
                HashSet<int> chromePids = new HashSet<int>();
                try
                {
                    foreach (Process pr in Process.GetProcesses())
                    {
                        string n = pr.ProcessName.ToLower();
                        if (n.Contains("chrome")) chromePids.Add(pr.Id);
                    }
                }
                catch { }
                if (chromePids.Count == 0) { Log("AppWindowExists: no chrome process"); return false; }

                Process p = new Process();
                p.StartInfo.FileName = "netstat";
                p.StartInfo.Arguments = "-ano";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                foreach (string line in output.Split('\n'))
                {
                    if (line.IndexOf("127.0.0.1:" + Port, StringComparison.Ordinal) >= 0 &&
                        line.ToUpper().Contains("ESTABLISHED"))
                    {
                        string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 4)
                        {
                            int pid;
                            if (int.TryParse(parts[parts.Length - 1], out pid) && chromePids.Contains(pid))
                            {
                                Log("AppWindowExists: chrome connected to port " + Port + " (pid " + pid + ")");
                                return true;
                            }
                        }
                    }
                }
                Log("AppWindowExists: no chrome connection");
            }
            catch (Exception ex) { Log("AppWindowExists error: " + ex.Message); }

            Log("AppWindowExists: not found");
            return false;
        }

        // 打开网页：应用模式或默认浏览器
        private void OpenWeb()
        {
            string url = "http://127.0.0.1:" + Port;
            if (AppModeOpen)
            {
                string chrome = FindChrome();
                if (chrome != null)
                {
                    try { Process.Start(chrome, "--app=" + url + " --profile-directory=Default"); return; }
                    catch { }
                }
            }
            try { Process.Start(url); } catch { }
        }

        // ---------------- 窗口拖拽调整大小 ----------------
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        private void EnableResize()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                HwndSource src = HwndSource.FromHwnd(hwnd);
                if (src != null) src.AddHook(new HwndSourceHook(WndProc));
            }
            catch { }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_NCHITTEST = 0x0084;
            const int border = 12;   // 覆盖 10px 阴影留白 + 热区
            if (msg == WM_NCHITTEST)
            {
                int sx = (short)(lParam.ToInt64() & 0xFFFF);
                int sy = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                try
                {
                    Point p = PointFromScreen(new Point(sx, sy));
                    bool top = p.Y <= border, bottom = p.Y >= ActualHeight - border;
                    bool left = p.X <= border, right = p.X >= ActualWidth - border;
                    if (top && left) { handled = true; return (IntPtr)13; }
                    if (top && right) { handled = true; return (IntPtr)14; }
                    if (bottom && left) { handled = true; return (IntPtr)16; }
                    if (bottom && right) { handled = true; return (IntPtr)17; }
                    if (top) { handled = true; return (IntPtr)12; }
                    if (bottom) { handled = true; return (IntPtr)15; }
                    if (left) { handled = true; return (IntPtr)10; }
                    if (right) { handled = true; return (IntPtr)11; }
                }
                catch { }
            }
            return IntPtr.Zero;
        }

        // ---------------- 服务启停 ----------------
        private void StartServer()
        {
            if (Starting) { Log("Start already in progress, ignored"); return; }
            // 端口占用诊断：区分"我们的服务在跑"和"被其他程序占用"
            if (PortAlive())
            {
                int lp = FindListenerPid();
                bool isNode = false;
                try
                {
                    Process pr = Process.GetProcessById(lp);
                    isNode = (pr.ProcessName.ToLower() == "node");
                }
                catch { }
                if (isNode)
                {
                    Log("Server already running");
                    return;
                }
                string name = "未知程序";
                try { Process pr = Process.GetProcessById(lp); name = pr.ProcessName + " (PID " + lp + ")"; } catch { }
                Log("PORT BUSY: port " + Port + " occupied by " + name);
                ShowConfirm("端口被占用", "端口 " + Port + " 已被进程 " + name + " 占用。\n请先停止该程序，或在设置中修改端口。", "OK");
                return;
            }
            Starting = true;
            StartingSince = DateTime.Now;
            UpdateStatusUi(false);
            Log("Starting dsh web... (首次启动可能需要 10-30 秒)");
            if (BinPath == null || !File.Exists(BinPath))
            {
                Log("ERROR: bin.js not found");
                ShowConfirm("服务无法启动", "未找到 dsh 程序 (bin.js)。\n请点击标题栏设置按钮，在「dsh 程序路径」中指定正确位置。", "OK");
                Starting = false;
                UpdateStatusUi(false);
                return;
            }
            Log("Starting dsh web...");
            ApplyVisionPatch();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(FindNode(), "\"" + BinPath + "\" --profile web --port " + Port + " --host 127.0.0.1");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                ServerProc = Process.Start(psi);
                ServerProc.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                        try { RotateIfLarge(ServerOut); File.AppendAllText(ServerOut, e.Data + "\n", Encoding.UTF8); } catch { }
                };
                ServerProc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                        try { RotateIfLarge(ServerErr); File.AppendAllText(ServerErr, e.Data + "\n", Encoding.UTF8); } catch { }
                };
                ServerProc.BeginOutputReadLine();
                ServerProc.BeginErrorReadLine();
                Log("Launched node pid=" + ServerProc.Id);
            }
            catch (Exception ex)
            {
                Log("Start failed: " + ex.Message);
                Starting = false;
                UpdateStatusUi(false);
                ShowConfirm("启动失败", "无法启动 Node.js：" + ex.Message + "\n\n可能原因：未安装 Node.js 或路径错误。\n建议使用安装包安装（自带运行环境），或在设置中指定 dsh 路径。", "OK");
            }

            if (AutoOpenWeb)
            {
                DispatcherTimer wt = new DispatcherTimer();
                wt.Interval = TimeSpan.FromMilliseconds(600);
                int tries = 0;
                wt.Tick += delegate(object s, EventArgs e)
                {
                    tries++;
                    if (PortAlive() || tries > 40)
                    {
                        wt.Stop();
                        if (PortAlive())
                        {
                            // 已有该网页的 Chrome 窗口则不重复打开（连接检测 + 窗口标题检测）
                            if (AppWindowExists())
                                Log("App window already open, skip auto-open");
                            else
                                OpenWeb();
                        }
                    }
                };
                wt.Start();
            }
        }

        private void StopServer()
        {
            LastManualStop = DateTime.Now;
            Starting = false;
            List<int> targets = new List<int>();
            int lp = FindListenerPid();
            if (lp > 0) targets.Add(lp);
            if (ServerProc != null && !ServerProc.HasExited) targets.Add(ServerProc.Id);
            foreach (int id in targets)
            {
                try
                {
                    Process pr = Process.GetProcessById(id);
                    pr.Kill();
                    Log("Stopped pid=" + id);
                }
                catch { }
            }
            ServerProc = null;
        }

        private int FindListenerPid()
        {
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "netstat";
                p.StartInfo.Arguments = "-ano";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                foreach (string line in output.Split('\n'))
                {
                    if (line.Contains(":" + Port) && line.ToUpper().Contains("LISTENING"))
                    {
                        string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 4)
                        {
                            int pid;
                            if (int.TryParse(parts[parts.Length - 1], out pid)) return pid;
                        }
                    }
                }
            }
            catch { }
            return -1;
        }

        private int GetListenerPidCached()
        {
            if (CachedPid > 0 && (DateTime.Now - LastPidCheck).TotalSeconds < 5)
            {
                try { Process.GetProcessById(CachedPid); return CachedPid; }
                catch { CachedPid = -1; }
            }
            CachedPid = FindListenerPid();
            LastPidCheck = DateTime.Now;
            return CachedPid;
        }

        private string GetListenAddressCached()
        {
            if (ListenAddr != "" && (DateTime.Now - LastAddrCheck).TotalSeconds < 10) return ListenAddr;
            LastAddrCheck = DateTime.Now;
            ListenAddr = "";
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "netstat";
                p.StartInfo.Arguments = "-ano";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                foreach (string line in output.Split('\n'))
                {
                    if (line.Contains(":" + Port) && line.ToUpper().Contains("LISTENING"))
                    {
                        string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 4)
                        {
                            string local = parts[1];
                            int idx = local.LastIndexOf(':');
                            if (idx > 0) { ListenAddr = local.Substring(0, idx); break; }
                        }
                    }
                }
            }
            catch { }
            return ListenAddr;
        }

        private bool IsExposedAddress(string addr)
        {
            if (String.IsNullOrEmpty(addr)) return false;
            string a = addr.Trim().ToLower();
            return a != "127.0.0.1" && a != "[::1]" && a != "localhost";
        }

        private string FindBin()
        {
            // 0. 更新功能下载的新版 dsh（dsh-update 优先）
            try
            {
                string updated = Path.Combine(AppDir, "dsh-update", "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
                if (File.Exists(updated)) return updated;
            }
            catch { }
            // 1. 安装目录捆绑的 dsh（安装器安装的）
            try
            {
                string bundled = Path.Combine(AppDir, "dsh", "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
                if (File.Exists(bundled)) return bundled;
            }
            catch { }
            // 2. npx 缓存
            try
            {
                string npxRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "npm-cache", "_npx");
                if (Directory.Exists(npxRoot))
                {
                    foreach (string d in Directory.GetDirectories(npxRoot))
                    {
                        string f = Path.Combine(d, "node_modules", "@deepseek-ai", "dsh", "lib", "bin.js");
                        if (File.Exists(f)) return f;
                    }
                }
            }
            catch { }
            return null;
        }

        // 查找 node：优先安装目录捆绑的 node.exe，其次系统 PATH
        private string FindNode()
        {
            try
            {
                string bundled = Path.Combine(AppDir, "node", "node.exe");
                if (File.Exists(bundled)) return bundled;
            }
            catch { }
            return "node";
        }

        // 启动服务前恢复 dsh vision patch（dsh 依赖升级可能覆盖）
        private void ApplyVisionPatch()
        {
            try
            {
                string psFile = Path.Combine(AppDir, "patches", "dsh-vision", "writeback.ps1");
                if (!File.Exists(psFile)) { Log("Vision patch: not found, skip"); return; }
                string powerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
                if (!File.Exists(powerShell)) powerShell = "powershell";
                string dshRoot = Path.Combine(AppDir, "dsh");
                string arg = "-NoProfile -ExecutionPolicy Bypass -File \"" + psFile + "\"";
                if (Directory.Exists(dshRoot)) arg += " -DshRoot \"" + dshRoot + "\"";
                ProcessStartInfo psi = new ProcessStartInfo(powerShell, arg);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Log("Vision patch: applying...");
                Process p = Process.Start(psi);
                if (p != null) { if (!p.WaitForExit(15000)) { try { p.Kill(); } catch { } } }
                Log("Vision patch: done");
            }
            catch (Exception ex) { Log("Vision patch skipped: " + ex.Message); }
        }

        // ---------------- UI 构建 ----------------
        private Border MakeButton(string text, string bgKey, int w, int h, Action click)
        {
            Border b = new Border();
            b.Width = w; b.Height = h;
            b.Cursor = Cursors.Hand;
            b.CornerRadius = new CornerRadius(8);
            TextBlock t = new TextBlock();
            t.Text = text;
            t.Foreground = Brushes.White;
            t.FontSize = 12.5;
            t.FontWeight = FontWeights.SemiBold;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            b.Child = t;
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { click(); };
            b.MouseEnter += delegate(object s, MouseEventArgs e) { b.Opacity = 0.85; };
            b.MouseLeave += delegate(object s, MouseEventArgs e) { b.Opacity = 1.0; };
            Repainters.Add(delegate() { b.Background = Theme.Brush(bgKey); });
            return b;
        }

        private Grid MakeTitleButton(char iconChar, string tip, Action click)
        {
            Grid g = new Grid();
            g.Width = 40; g.Height = 40;
            g.ToolTip = tip;
            TextBlock t = new TextBlock();
            t.Text = iconChar.ToString();
            t.FontFamily = new FontFamily("Segoe MDL2 Assets");
            t.FontSize = 14;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Cursor = Cursors.Hand;
            g.Children.Add(t);
            g.MouseEnter += delegate(object s, MouseEventArgs e) { g.Background = Theme.Brush("HoverBg"); };
            g.MouseLeave += delegate(object s, MouseEventArgs e) { g.Background = Brushes.Transparent; };
            g.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { click(); };
            Repainters.Add(delegate() { t.Foreground = Theme.Brush("FgSecondary"); });
            return g;
        }

        private void BuildUi()
        {
            Title = "DeepSeek Harness Launcher";
            Width = 480; Height = 826;
            MinWidth = 420; MinHeight = 620;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = true;
            FontFamily = new FontFamily(Theme.FontFamily);
            try { Icon = BitmapFrame.Create(new Uri(IcoPath)); } catch { }
            EnableResize();

            Border outer = new Border();
            outer.Margin = new Thickness(10);
            outer.CornerRadius = new CornerRadius(14);
            outer.BorderThickness = new Thickness(1);
            outer.Effect = new DropShadowEffect() { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 6, Opacity = 0.55 };
            Repainters.Add(delegate()
            {
                outer.Background = Theme.Brush("BgWindow");
                outer.BorderBrush = Theme.Brush("BorderW");
            });
            Content = outer;

            Grid root = new Grid();
            outer.Child = root;
            root.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(40) });
            root.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });

            // ===== 标题栏 =====
            Grid titleBar = new Grid();
            titleBar.Background = Brushes.Transparent;
            titleBar.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                try { DragMove(); } catch { }
            };
            root.Children.Add(titleBar);

            StackPanel titleLeft = new StackPanel();
            titleLeft.Orientation = Orientation.Horizontal;
            titleLeft.Margin = new Thickness(16, 0, 0, 0);
            titleLeft.VerticalAlignment = VerticalAlignment.Center;
            Ellipse dot = new Ellipse();
            dot.Width = 10; dot.Height = 10;
            dot.VerticalAlignment = VerticalAlignment.Center;
            Repainters.Add(delegate() { dot.Fill = Theme.Brush("Accent"); });
            titleLeft.Children.Add(dot);
            TextBlock t1 = new TextBlock();
            t1.Text = "DeepSeek Harness";
            t1.FontSize = 13; t1.FontWeight = FontWeights.SemiBold;
            t1.Margin = new Thickness(8, 0, 0, 0);
            t1.VerticalAlignment = VerticalAlignment.Center;
            Repainters.Add(delegate() { t1.Foreground = Theme.Brush("FgPrimary"); });
            titleLeft.Children.Add(t1);
            TextBlock t2 = new TextBlock();
            t2.Text = "服务管理器";
            t2.FontSize = 11;
            t2.Margin = new Thickness(10, 0, 0, 0);
            t2.VerticalAlignment = VerticalAlignment.Center;
            Repainters.Add(delegate() { t2.Foreground = Theme.Brush("FgMuted"); });
            titleLeft.Children.Add(t2);
            titleBar.Children.Add(titleLeft);

            StackPanel titleRight = new StackPanel();
            titleRight.Orientation = Orientation.Horizontal;
            titleRight.HorizontalAlignment = HorizontalAlignment.Right;
            titleRight.VerticalAlignment = VerticalAlignment.Top;
            BtnSettings = MakeTitleButton((char)0xE713, "设置（dsh 路径 / 服务 / 外观 / 卸载）", delegate() { ShowSettingsDialog(); });
            BtnTheme = MakeTitleButton((char)0xE706, "切换主题（深色 / 浅色）", delegate()
            {
                Theme.Light = !Theme.Light;
                FollowSystem = false;
                Settings.SetString("followSystem", "off");
                Settings.SetString("theme", Theme.Light ? "light" : "dark");
                ApplyTheme();
                SavePosition();
                UpdateThemeGlyph();
            });
            ThemeGlyphText = (TextBlock)BtnTheme.Children[0];
            BtnMin = MakeTitleButton((char)0xE921, "最小化到任务栏", delegate() { WindowState = WindowState.Minimized; });
            BtnClose = MakeTitleButton((char)0xE8BB, "关闭到系统托盘", delegate() { SavePosition(); HideToTray("Window closed to tray (service stays running)"); });
            BtnSettings.Margin = new Thickness(0, 0, 2, 0);
            BtnTheme.Margin = new Thickness(0, 0, 2, 0);
            BtnMin.Margin = new Thickness(0, 0, 2, 0);
            titleRight.Children.Add(BtnSettings);
            titleRight.Children.Add(BtnTheme);
            titleRight.Children.Add(BtnMin);
            titleRight.Children.Add(BtnClose);
            titleBar.Children.Add(titleRight);

            // ===== 状态卡 =====
            Border statusCard = new Border();
            statusCard.Margin = new Thickness(16, 4, 16, 0);
            statusCard.CornerRadius = new CornerRadius(12);
            statusCard.BorderThickness = new Thickness(1);
            statusCard.Padding = new Thickness(16, 14, 16, 14);
            Repainters.Add(delegate()
            {
                statusCard.Background = Theme.Brush("BgCard");
                statusCard.BorderBrush = Theme.Brush("BorderW");
            });
            Grid.SetRow(statusCard, 1);
            root.Children.Add(statusCard);

            Grid scGrid = new Grid();
            statusCard.Child = scGrid;
            scGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            scGrid.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });

            StackPanel scLeft = new StackPanel();
            scGrid.Children.Add(scLeft);
            StackPanel row0 = new StackPanel();
            row0.Orientation = Orientation.Horizontal;
            DotStatus = new Ellipse();
            DotStatus.Width = 10; DotStatus.Height = 10;
            DotStatus.VerticalAlignment = VerticalAlignment.Center;
            DotStatus.Effect = new DropShadowEffect() { Color = Color.FromRgb(34, 197, 94), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.9 };
            DotStatus.Fill = Theme.Brush("FgMuted");
            row0.Children.Add(DotStatus);
            TxtStatus = new TextBlock();
            TxtStatus.Text = "检查中…";
            TxtStatus.FontSize = 16; TxtStatus.FontWeight = FontWeights.SemiBold;
            TxtStatus.Margin = new Thickness(9, 0, 0, 0);
            Repainters.Add(delegate() { TxtStatus.Foreground = Theme.Brush("FgPrimary"); });
            row0.Children.Add(TxtStatus);
            scLeft.Children.Add(row0);

            TxtUrl = new TextBlock();
            TxtUrl.Text = "http://127.0.0.1:" + Port;
            TxtUrl.FontFamily = new FontFamily("Consolas");
            TxtUrl.FontSize = 12.5;
            TxtUrl.Margin = new Thickness(19, 8, 0, 0);
            Repainters.Add(delegate() { TxtUrl.Foreground = Theme.Brush("LinkBlue"); });
            scLeft.Children.Add(TxtUrl);

            TxtMeta = new TextBlock();
            TxtMeta.Text = "PID -- · 端口 " + Port;
            TxtMeta.FontFamily = new FontFamily("Consolas");
            TxtMeta.FontSize = 11;
            TxtMeta.Margin = new Thickness(19, 5, 0, 0);
            Repainters.Add(delegate() { TxtMeta.Foreground = Theme.Brush("FgMuted"); });
            scLeft.Children.Add(TxtMeta);

            TxtWarn = new TextBlock();
            TxtWarn.Text = "⚠ 服务监听在 0.0.0.0，已暴露在局域网！";
            TxtWarn.FontSize = 11;
            TxtWarn.FontWeight = FontWeights.SemiBold;
            TxtWarn.Margin = new Thickness(19, 5, 0, 0);
            TxtWarn.Visibility = Visibility.Collapsed;
            Repainters.Add(delegate() { TxtWarn.Foreground = Theme.Brush("Amber"); });
            scLeft.Children.Add(TxtWarn);

            BtnToggle = MakeButton("启动服务", "Accent", 104, 36, delegate()
            {
                if (Starting) return;   // 启动中防重复点击
                if (PortAlive())
                {
                    string r = ShowConfirm("停止服务", "确定停止 DeepSeek Harness 服务？\n正在使用的会话将断开。", "YesNo");
                    if (r == "Yes") { StopServer(); Log("Stop requested from UI"); }
                }
                else
                {
                    StartServer();
                    Log("Start requested from UI");
                }
            });
            BtnToggle.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(BtnToggle, 1);
            scGrid.Children.Add(BtnToggle);

            // ===== 指标行 =====
            Grid metrics = new Grid();
            metrics.Margin = new Thickness(16, 10, 16, 0);
            Grid.SetRow(metrics, 2);
            root.Children.Add(metrics);
            for (int i = 0; i < 7; i++)
                metrics.ColumnDefinitions.Add(new ColumnDefinition() { Width = (i % 2 == 1) ? new GridLength(8) : new GridLength(1, GridUnitType.Star) });
            string[] labels = { "CPU", "内存", "端口", "进程" };
            int[] cols = { 0, 2, 4, 6 };
            TextBlock[] vals = new TextBlock[4];
            for (int i = 0; i < 4; i++)
            {
                Border card = new Border();
                card.CornerRadius = new CornerRadius(10);
                card.Padding = new Thickness(12, 8, 12, 8);
                Grid.SetColumn(card, cols[i]);
                Repainters.Add(delegate() { card.Background = Theme.Brush("BgCard"); });
                metrics.Children.Add(card);
                StackPanel sp = new StackPanel();
                card.Child = sp;
                TextBlock lb = new TextBlock();
                lb.Text = labels[i];
                lb.FontSize = 10;
                Repainters.Add(delegate() { lb.Foreground = Theme.Brush("FgMuted"); });
                sp.Children.Add(lb);
                TextBlock v = new TextBlock();
                v.Text = (i == 2) ? Port.ToString() : "--";
                v.FontSize = 14; v.FontWeight = FontWeights.SemiBold;
                v.Margin = new Thickness(0, 3, 0, 0);
                Repainters.Add(delegate() { v.Foreground = Theme.Brush("FgPrimary"); });
                sp.Children.Add(v);
                vals[i] = v;
            }
            TxtCpu = vals[0]; TxtMem = vals[1]; TxtPort = vals[2]; TxtProc = vals[3];

            // ===== 趋势图（可折叠） =====
            TrendCard = new Border();
            TrendCard.Height = 112;
            TrendCard.Margin = new Thickness(16, 10, 16, 0);
            TrendCard.CornerRadius = new CornerRadius(10);
            TrendCard.BorderThickness = new Thickness(1);
            TrendCard.Padding = new Thickness(12, 8, 12, 8);
            Repainters.Add(delegate()
            {
                TrendCard.Background = Theme.Brush("BgCard");
                TrendCard.BorderBrush = Theme.Brush("BorderW");
            });
            Grid.SetRow(TrendCard, 3);
            root.Children.Add(TrendCard);
            Grid tg = new Grid();
            TrendCard.Child = tg;
            tg.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            tg.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            Grid tgHead = new Grid();
            tg.Children.Add(tgHead);
            TextBlock tgTitle = new TextBlock();
            tgTitle.Text = "资源趋势 (60 秒)";
            tgTitle.FontSize = 11;
            tgTitle.VerticalAlignment = VerticalAlignment.Center;
            Repainters.Add(delegate() { tgTitle.Foreground = Theme.Brush("FgSecondary"); });
            tgHead.Children.Add(tgTitle);
            StackPanel tgRight = new StackPanel();
            tgRight.Orientation = Orientation.Horizontal;
            tgRight.HorizontalAlignment = HorizontalAlignment.Right;
            tgRight.VerticalAlignment = VerticalAlignment.Center;
            TrendLegend = new TextBlock();
            TrendLegend.FontSize = 10.5;
            TrendLegend.VerticalAlignment = VerticalAlignment.Center;
            Repainters.Add(delegate() { TrendLegend.Foreground = Theme.Brush("FgMuted"); });
            tgRight.Children.Add(TrendLegend);
            TrendArrow = new TextBlock();
            TrendArrow.Text = "▾";
            TrendArrow.FontSize = 11;
            TrendArrow.Width = 24; TrendArrow.Height = 24;
            TrendArrow.TextAlignment = TextAlignment.Center;
            TrendArrow.VerticalAlignment = VerticalAlignment.Center;
            TrendArrow.Margin = new Thickness(10, 0, 0, 0);
            TrendArrow.Cursor = Cursors.Hand;
            TrendArrow.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { ToggleTrend(); };
            Repainters.Add(delegate() { TrendArrow.Foreground = Theme.Brush("FgSecondary"); });
            tgRight.Children.Add(TrendArrow);
            tgHead.Children.Add(tgRight);
            TrendCanvas = new Canvas();
            TrendCanvas.Margin = new Thickness(0, 6, 0, 0);
            TrendCanvas.Background = Theme.Brush("BgLog");
            Repainters.Add(delegate() { TrendCanvas.Background = Theme.Brush("BgLog"); });
            Grid.SetRow(TrendCanvas, 1);
            tg.Children.Add(TrendCanvas);

            // ===== 日志面板 =====
            Border logCard = new Border();
            logCard.Margin = new Thickness(16, 12, 16, 0);
            logCard.CornerRadius = new CornerRadius(10);
            logCard.BorderThickness = new Thickness(1);
            Repainters.Add(delegate()
            {
                logCard.Background = Theme.Brush("BgLog");
                logCard.BorderBrush = Theme.Brush("BorderW");
            });
            Grid.SetRow(logCard, 4);
            root.Children.Add(logCard);

            Grid lg = new Grid();
            logCard.Child = lg;
            lg.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            lg.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });

            Grid lgHead = new Grid();
            lgHead.Margin = new Thickness(14, 9, 10, 7);
            lg.Children.Add(lgHead);
            TextBlock lgTitle = new TextBlock();
            lgTitle.Text = "运行日志";
            lgTitle.FontSize = 11;
            lgTitle.VerticalAlignment = VerticalAlignment.Center;
            Repainters.Add(delegate() { lgTitle.Foreground = Theme.Brush("FgSecondary"); });
            lgHead.Children.Add(lgTitle);
            StackPanel lgRight = new StackPanel();
            lgRight.Orientation = Orientation.Horizontal;
            lgRight.HorizontalAlignment = HorizontalAlignment.Right;
            TextBlock hlLb = new TextBlock();
            hlLb.Text = "高亮";
            hlLb.FontSize = 10.5;
            hlLb.VerticalAlignment = VerticalAlignment.Center;
            hlLb.Margin = new Thickness(0, 0, 6, 0);
            Repainters.Add(delegate() { hlLb.Foreground = Theme.Brush("FgMuted"); });
            lgRight.Children.Add(hlLb);
            TxtHighlight = new TextBox();
            TxtHighlight.Width = 86; TxtHighlight.Height = 22;
            TxtHighlight.FontSize = 10.5;
            TxtHighlight.VerticalContentAlignment = VerticalAlignment.Center;
            TxtHighlight.Padding = new Thickness(6, 0, 6, 0);
            TxtHighlight.BorderThickness = new Thickness(1);
            Repainters.Add(delegate()
            {
                TxtHighlight.Background = Brushes.Transparent;
                TxtHighlight.Foreground = Theme.Brush("FgPrimary");
                TxtHighlight.CaretBrush = Theme.Brush("FgPrimary");
                TxtHighlight.BorderBrush = Theme.Brush("BorderW");
            });
            TxtHighlight.TextChanged += delegate(object s, TextChangedEventArgs e) { UpdateLog(); };
            lgRight.Children.Add(TxtHighlight);
            Border btnExport = MakeButton("导出", "BgCardAlt", 44, 22, delegate()
            {
                WF.SaveFileDialog sfd = new WF.SaveFileDialog();
                sfd.Title = "导出日志";
                sfd.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";
                sfd.FileName = "dsh-logs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
                if (sfd.ShowDialog() == WF.DialogResult.OK)
                {
                    try
                    {
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine("===== DeepSeek Harness 日志导出 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " =====");
                        sb.AppendLine("");
                        sb.AppendLine("[launcher]");
                        sb.AppendLine(File.Exists(LauncherLog) ? File.ReadAllText(LauncherLog, Encoding.UTF8) : "(空)");
                        sb.AppendLine("");
                        sb.AppendLine("[server stdout]");
                        sb.AppendLine(File.Exists(ServerOut) ? File.ReadAllText(ServerOut, Encoding.UTF8) : "(空)");
                        sb.AppendLine("");
                        sb.AppendLine("[server stderr]");
                        sb.AppendLine(File.Exists(ServerErr) ? File.ReadAllText(ServerErr, Encoding.UTF8) : "(空)");
                        File.WriteAllText(sfd.FileName, sb.ToString(), Encoding.UTF8);
                        TxtFooter.Text = "日志已导出";
                        DispatcherTimer t = new DispatcherTimer();
                        t.Interval = TimeSpan.FromMilliseconds(1500);
                        t.Tick += delegate(object s2, EventArgs e2) { TxtFooter.Text = FooterVer; t.Stop(); };
                        t.Start();
                    }
                    catch { }
                }
            });
            btnExport.Margin = new Thickness(6, 0, 0, 0);
            btnExport.CornerRadius = new CornerRadius(5);
            lgRight.Children.Add(btnExport);
            BtnClearLog = MakeButton("清空", "BgCardAlt", 44, 22, delegate()
            {
                try
                {
                    if (File.Exists(ServerOut)) File.WriteAllText(ServerOut, "");
                    if (File.Exists(ServerErr)) File.WriteAllText(ServerErr, "");
                }
                catch { }
                LastLogText = "";
                UpdateLog();
            });
            BtnClearLog.Margin = new Thickness(6, 0, 0, 0);
            BtnClearLog.CornerRadius = new CornerRadius(5);
            lgRight.Children.Add(BtnClearLog);
            lgHead.Children.Add(lgRight);

            TxtLog = new RichTextBox();
            TxtLog.IsReadOnly = true;
            TxtLog.BorderThickness = new Thickness(0);
            TxtLog.Margin = new Thickness(6, 0, 6, 8);
            TxtLog.FontFamily = new FontFamily("Consolas");
            TxtLog.FontSize = 11;
            TxtLog.Padding = new Thickness(8, 4, 8, 4);
            TxtLog.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            TxtLog.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            Repainters.Add(delegate()
            {
                TxtLog.Background = Brushes.Transparent;
                TxtLog.Foreground = Theme.Brush("FgLog");
            });
            Grid.SetRow(TxtLog, 1);
            lg.Children.Add(TxtLog);

            // ===== 更新进度条（主窗口常驻，跨窗口可见）=====
            UpdRow = new Grid();
            UpdRow.Margin = new Thickness(16, 4, 16, 0);
            UpdRow.Visibility = Visibility.Collapsed;
            Grid.SetRow(UpdRow, 5);
            root.Children.Add(UpdRow);
            UpdRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            UpdRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            UpdProgMain = new ProgressBar();
            UpdProgMain.Height = 5;
            UpdProgMain.Foreground = Theme.Brush("Accent");
            UpdProgMain.Background = Theme.Brush("BgAlt");
            UpdProgMain.Minimum = 0; UpdProgMain.Maximum = 100;
            UpdProgMain.VerticalAlignment = VerticalAlignment.Center;
            UpdRow.Children.Add(UpdProgMain);
            UpdStatusMain = new TextBlock();
            UpdStatusMain.FontSize = 10.5;
            UpdStatusMain.Margin = new Thickness(10, 0, 0, 0);
            UpdStatusMain.Foreground = Theme.Brush("Amber");
            UpdStatusMain.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(UpdStatusMain, 1);
            UpdRow.Children.Add(UpdStatusMain);

            // ===== 底部 =====
            Grid footer = new Grid();
            footer.Margin = new Thickness(16, 12, 16, 16);
            Grid.SetRow(footer, 6);
            root.Children.Add(footer);
            footer.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });

            StackPanel footerLeft = new StackPanel();
            footerLeft.Orientation = Orientation.Horizontal;
            footerLeft.VerticalAlignment = VerticalAlignment.Center;
            TglAutoStart = MakeToggle();
            footerLeft.Children.Add(TglAutoStart);
            TextBlock asLb = new TextBlock();
            asLb.Text = "开机自启";
            asLb.FontSize = 12;
            asLb.Margin = new Thickness(8, 0, 0, 0);
            asLb.VerticalAlignment = VerticalAlignment.Center;
            Repainters.Add(delegate() { asLb.Foreground = Theme.Brush("FgSecondary"); });
            footerLeft.Children.Add(asLb);
            TxtFooter = new TextBlock();
            TxtFooter.FontSize = 10.5;
            TxtFooter.Margin = new Thickness(14, 1, 0, 0);
            TxtFooter.VerticalAlignment = VerticalAlignment.Center;
            Repainters.Add(delegate() { TxtFooter.Foreground = Theme.Brush("FgMuted"); });
            footerLeft.Children.Add(TxtFooter);
            footer.Children.Add(footerLeft);

            StackPanel footerRight = new StackPanel();
            footerRight.Orientation = Orientation.Horizontal;
            footerRight.VerticalAlignment = VerticalAlignment.Center;
            BtnCopy = MakeButton("复制地址", "BgCardAlt", 78, 32, delegate()
            {
                try { Clipboard.SetText("http://127.0.0.1:" + Port); } catch { }
                TxtFooter.Text = "已复制";
                DispatcherTimer t = new DispatcherTimer();
                t.Interval = TimeSpan.FromMilliseconds(1200);
                t.Tick += delegate(object s, EventArgs e) { TxtFooter.Text = FooterVer; t.Stop(); };
                t.Start();
            });
            BtnCopy.Margin = new Thickness(0, 0, 8, 0);
            footerRight.Children.Add(BtnCopy);
            BtnOpen = MakeButton("打开网页", "Accent", 88, 32, delegate() { OpenWeb(); });
            footerRight.Children.Add(BtnOpen);
            Grid.SetColumn(footerRight, 1);
            footer.Children.Add(footerRight);

            Closing += delegate(object s, System.ComponentModel.CancelEventArgs e)
            {
                if (!ReallyExit)
                {
                    e.Cancel = true;
                    SavePosition();
                    HideToTray("Window closed (Alt+F4) to tray");
                }
                else
                {
                    if (!SkipSaveSettings) SavePosition();
                }
            };
        }

        private string FooterVer = "";
        private void InitFooter()
        {
            string ver = "";
            try
            {
                if (BinPath != null)
                {
                    string pkg = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(BinPath)), "package.json");
                    if (File.Exists(pkg))
                    {
                        string txt = File.ReadAllText(pkg, Encoding.UTF8);
                        Match m = Regex.Match(txt, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                        if (m.Success) ver = "v" + m.Groups[1].Value;
                    }
                }
            }
            catch { }
            FooterVer = ver;
            TxtFooter.Text = ver;
        }

        // ---------------- 检查 / 更新 dsh ----------------
        // 查询最新版本（后台可调用，不碰 UI）
        private string QueryLatestVersion()
        {
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "cmd.exe";
                p.StartInfo.Arguments = "/c npm view @deepseek-ai/dsh version --registry=https://registry.npmmirror.com";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.Start();
                System.Threading.Tasks.Task<string> outTask = p.StandardOutput.ReadToEndAsync();
                System.Threading.Tasks.Task<string> errTask = p.StandardError.ReadToEndAsync();
                p.WaitForExit(45000);
                string output = outTask.Result;
                string errout = errTask.Result;
                if (output.Trim().Length == 0)
                    Log("QueryLatest failed: " + (errout.Length > 200 ? errout.Substring(0, 200) : errout));
                return output.Trim();
            }
            catch (Exception ex) { Log("QueryLatest error: " + ex.Message); return ""; }
        }

        // 启动时自动检查（后台，发现新版则提示 + 更新按钮变蓝）
        private void AutoCheckUpdate()
        {
            System.Threading.Tasks.Task.Factory.StartNew(delegate()
            {
                string latest = QueryLatestVersion();
                string cur = FooterVer.StartsWith("v") ? FooterVer.Substring(1) : FooterVer;
                bool hasNew = (latest.Length > 0 && latest != cur);
                Dispatcher.BeginInvoke(new Action(delegate()
                {
                    if (hasNew)
                    {
                        UpdateAvailable = true;
                        UpdateLatestVer = latest;
                        TxtFooter.Text = "发现新版本 v" + latest;
                        Log("Update available: " + latest);
                    }
                }));
            });
        }

        // 手动检查（设置窗口按钮）
        private void CheckUpdate()
        {
            if (UpdateLatestVer.Length > 0) { ShowUpdatePrompt(UpdateLatestVer); return; }
            TxtFooter.Text = "正在检查更新…";
            System.Threading.Tasks.Task.Factory.StartNew(delegate()
            {
                string latest = QueryLatestVersion();
                string cur = FooterVer.StartsWith("v") ? FooterVer.Substring(1) : FooterVer;
                Dispatcher.BeginInvoke(new Action(delegate()
                {
                    TxtFooter.Text = FooterVer;
                    if (latest.Length == 0)
                    {
                        ShowConfirm("检查更新", "无法获取最新版本（网络或镜像不可用）。\n请确认网络正常后重试。", "OK");
                        return;
                    }
                    if (latest == cur)
                        ShowConfirm("检查更新", "当前已是最新版本（" + cur + "）。", "OK");
                    else
                    {
                        UpdateAvailable = true;
                        UpdateLatestVer = latest;
                        ShowUpdatePrompt(latest);
                    }
                }));
            });
        }

        private void ShowUpdatePrompt(string latest)
        {
            string cur = FooterVer.StartsWith("v") ? FooterVer.Substring(1) : FooterVer;
            string r = ShowConfirm("发现新版本", "当前版本: " + cur + "\n最新版本: " + latest + "\n\n是否立即更新？\n将下载新版 dsh，更新后需重启服务生效。", "YesNo");
            if (r == "Yes") DoUpdate();
        }

        private void DoUpdate()
        {
            // 后台异步：下载新版 dsh 包 -> 直接覆盖现有 dsh（复用已有依赖，不做 npm 依赖安装）
            Log("Update: downloading dsh...");
            TxtFooter.Text = "正在更新 dsh…";
            if (UpdRow != null) UpdRow.Visibility = Visibility.Visible;
            if (UpdProgMain != null) UpdProgMain.Value = 0;
            if (UpdStatusMain != null) UpdStatusMain.Text = "准备中…";
            if (BtnUpdateBtn != null) BtnUpdateBtn.Opacity = 0.5;

            System.Threading.Tasks.Task.Factory.StartNew(delegate()
            {
                string errInfo = "";
                bool ok = false;
                string newVer = "";
                string pkgDir = "";
                string backup = "";
                string extractDir = "";
                string tarball = "";
                try
                {
                    // 目标：当前 dsh 包目录（node_modules/@deepseek-ai/dsh）
                    if (BinPath == null || !File.Exists(BinPath)) throw new Exception("未找到当前 dsh 位置");
                    pkgDir = Path.GetDirectoryName(Path.GetDirectoryName(BinPath));
                    if (String.IsNullOrEmpty(pkgDir) || !Directory.Exists(pkgDir)) throw new Exception("dsh 包目录无效");

                    // 1. 停止服务（避免文件占用）
                    try { StopServer(); } catch { }

                    // 2. 获取 tarball 下载地址
                    string tarballUrl = "";
                    try
                    {
                        Process pv = new Process();
                        pv.StartInfo.FileName = "cmd.exe";
                        pv.StartInfo.Arguments = "/c npm view @deepseek-ai/dsh dist.tarball --registry=https://registry.npmmirror.com";
                        pv.StartInfo.UseShellExecute = false;
                        pv.StartInfo.CreateNoWindow = true;
                        pv.StartInfo.RedirectStandardOutput = true;
                        pv.StartInfo.RedirectStandardError = true;
                        pv.Start();
                        System.Threading.Tasks.Task<string> oT = pv.StandardOutput.ReadToEndAsync();
                        System.Threading.Tasks.Task<string> eT = pv.StandardError.ReadToEndAsync();
                        pv.WaitForExit(30000);
                        tarballUrl = oT.Result.Trim();
                    }
                    catch { }
                    if (tarballUrl.Length == 0) throw new Exception("无法获取下载地址");

                    // 3. 下载 tarball（真实字节进度 0-70%）
                    tarball = Path.Combine(Path.GetTempPath(), "dsh-latest.tgz");
                    using (System.Net.WebClient wc = new System.Net.WebClient())
                    {
                        wc.DownloadProgressChanged += delegate(object s, System.Net.DownloadProgressChangedEventArgs e)
                        {
                            int pct = (int)(e.ProgressPercentage * 0.7);
                            Dispatcher.BeginInvoke(new Action(delegate()
                            {
                                if (UpdProgMain != null) UpdProgMain.Value = pct;
                                if (UpdStatusMain != null) UpdStatusMain.Text = "正在下载 " + pct + "%";
                            }));
                        };
                        wc.DownloadFile(tarballUrl, tarball);
                    }

                    // 4. 解压 tarball（Windows 自带 tar）
                    extractDir = Path.Combine(Path.GetTempPath(), "dsh-extract-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    Directory.CreateDirectory(extractDir);
                    SetUpdateStage(78, "正在解压…");
                    Process tar = new Process();
                    tar.StartInfo.FileName = "cmd.exe";
                    tar.StartInfo.Arguments = "/c tar -xzf \"" + tarball + "\" -C \"" + extractDir + "\"";
                    tar.StartInfo.UseShellExecute = false;
                    tar.StartInfo.CreateNoWindow = true;
                    tar.StartInfo.RedirectStandardOutput = true;
                    tar.StartInfo.RedirectStandardError = true;
                    tar.Start();
                    System.Threading.Tasks.Task<string> tarOut = tar.StandardOutput.ReadToEndAsync();
                    System.Threading.Tasks.Task<string> tarErr = tar.StandardError.ReadToEndAsync();
                    bool tarExited = tar.WaitForExit(60000);
                    if (!tarExited) { try { tar.Kill(); } catch { } throw new Exception("解压超时"); }
                    string extractedPkg = Path.Combine(extractDir, "package");
                    if (!Directory.Exists(extractedPkg)) throw new Exception("包解压失败");

                    // 5. 备份并覆盖现有 dsh 包
                    SetUpdateStage(88, "正在应用更新…");
                    backup = pkgDir + ".bak";
                    if (Directory.Exists(backup)) Directory.Delete(backup, true);
                    Directory.Move(pkgDir, backup);
                    try
                    {
                        CopyDir(extractedPkg, pkgDir);
                    }
                    catch
                    {
                        // 回滚
                        try
                        {
                            if (Directory.Exists(pkgDir)) Directory.Delete(pkgDir, true);
                            Directory.Move(backup, pkgDir);
                        }
                        catch { }
                        throw;
                    }
                    try { if (Directory.Exists(backup)) Directory.Delete(backup, true); } catch { }

                    // 6. 读新版本
                    string pkgJson = Path.Combine(pkgDir, "package.json");
                    if (File.Exists(pkgJson))
                    {
                        string txt = File.ReadAllText(pkgJson, Encoding.UTF8);
                        Match m = Regex.Match(txt, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                        if (m.Success) newVer = m.Groups[1].Value;
                    }
                    ok = File.Exists(Path.Combine(pkgDir, "lib", "bin.js"));
                }
                catch (Exception ex)
                {
                    errInfo = ex.Message;
                    ok = false;
                    // 失败时尝试恢复备份
                    try
                    {
                        if (backup.Length > 0 && Directory.Exists(backup) && pkgDir.Length > 0 && !Directory.Exists(pkgDir))
                            Directory.Move(backup, pkgDir);
                    }
                    catch { }
                }
                // 清理临时
                try { if (extractDir.Length > 0 && Directory.Exists(extractDir)) Directory.Delete(extractDir, true); } catch { }
                try { if (tarball.Length > 0 && File.Exists(tarball)) File.Delete(tarball); } catch { }

                string logMsg = ok ? ("Update: dsh replaced to " + newVer) : ("Update failed: " + (errInfo.Length > 300 ? errInfo.Substring(0, 300) : errInfo));
                Dispatcher.BeginInvoke(new Action(delegate()
                {
                    TxtFooter.Text = FooterVer;
                    if (UpdRow != null) UpdRow.Visibility = Visibility.Collapsed;
                    if (UpdProgMain != null) UpdProgMain.Value = 0;
                    if (BtnUpdateBtn != null) BtnUpdateBtn.Opacity = 1.0;
                    Log(logMsg);
                    if (ok)
                    {
                        UpdateAvailable = false;
                        UpdateLatestVer = "";
                        ShowConfirm("更新完成", "dsh 已更新到 " + newVer + "。\n启动服务后生效。", "OK");
                    }
                    else
                        ShowConfirm("更新失败", "更新未完成（" + errInfo + "）。", "OK");
                }));
            });
        }

        // 更新阶段提示（后台线程调用，UI 经 Dispatcher）
        private void SetUpdateStage(int pct, string msg)
        {
            Dispatcher.BeginInvoke(new Action(delegate()
            {
                if (UpdProgMain != null) UpdProgMain.Value = pct;
                if (UpdStatusMain != null) UpdStatusMain.Text = msg;
            }));
        }

        private Border MakeToggle()
        {
            Border track = new Border();
            track.Width = 36; track.Height = 20;
            track.CornerRadius = new CornerRadius(10);
            track.Cursor = Cursors.Hand;
            Ellipse thumb = new Ellipse();
            thumb.Width = 14; thumb.Height = 14;
            thumb.Fill = Brushes.White;
            thumb.HorizontalAlignment = HorizontalAlignment.Left;
            thumb.Margin = new Thickness(3, 0, 0, 0);
            track.Child = thumb;
            Action toggleApply = delegate()
            {
                track.Background = Theme.Brush(AutoStartChecked ? "Accent" : "ToggleOff");
                thumb.HorizontalAlignment = AutoStartChecked ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                thumb.Margin = AutoStartChecked ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
            };
            Repainters.Add(toggleApply);
            AutoStartApply = toggleApply;
            track.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                AutoStartChecked = !AutoStartChecked;
                SetAutoStart(AutoStartChecked);
                toggleApply();
                TxtFooter.Text = AutoStartChecked ? "已开启开机自启" : "已关闭开机自启";
                DispatcherTimer t = new DispatcherTimer();
                t.Interval = TimeSpan.FromMilliseconds(1500);
                t.Tick += delegate(object s2, EventArgs e2) { TxtFooter.Text = FooterVer; t.Stop(); };
                t.Start();
            };
            return track;
        }

        private void ToggleTrend()
        {
            TrendCollapsed = !TrendCollapsed;
            Settings.SetString("trendCollapsed", TrendCollapsed ? "on" : "off");
            ApplyTrendState();
        }

        private void ApplyTrendState()
        {
            if (TrendCard == null) return;
            TrendCard.Height = TrendCollapsed ? 36 : 112;
            TrendCanvas.Visibility = TrendCollapsed ? Visibility.Collapsed : Visibility.Visible;
            TrendArrow.Text = TrendCollapsed ? "▸" : "▾";
        }

        // ---------------- 主题 ----------------
        private void ApplyTheme()
        {
            foreach (Action a in Repainters) a();
            UpdateThemeGlyph();
            LogMTime1 = LogMTime2 = LogMTime3 = DateTime.MinValue;
            LastLogText = "";
            UpdateLog();
        }

        private void UpdateThemeGlyph()
        {
            if (ThemeGlyphText != null)
                ThemeGlyphText.Text = Theme.Light ? ((char)0xE708).ToString() : ((char)0xE706).ToString();
        }

        // ---------------- 位置 ----------------
        private void RestorePosition()
        {
            int left = Settings.GetInt("left", -1);
            int top = Settings.GetInt("top", -1);
            int w = Settings.GetInt("width", 0);
            int h = Settings.GetInt("height", 0);
            if (w >= MinWidth) Width = w;
            if (h >= MinHeight) Height = h;
            if (left > -1 && top > -1)
            {
                double vsL = SystemParameters.VirtualScreenLeft;
                double vsT = SystemParameters.VirtualScreenTop;
                double vsW = SystemParameters.VirtualScreenWidth;
                double vsH = SystemParameters.VirtualScreenHeight;
                if (left >= vsL && top >= vsT && left < vsL + vsW - 160 && top < vsT + vsH - 120)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = left; Top = top;
                    return;
                }
            }
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        private void SavePosition()
        {
            try
            {
                Settings.SetInt("left", (int)Math.Round(Left));
                Settings.SetInt("top", (int)Math.Round(Top));
                Settings.SetInt("width", (int)Math.Round(Width));
                Settings.SetInt("height", (int)Math.Round(Height));
                Settings.SetString("theme", Theme.Light ? "light" : "dark");
            }
            catch { }
        }

        // ---------------- 开机自启 ----------------
        private void SetAutoStart(bool on)
        {
            try
            {
                RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (k == null) return;
                if (on)
                    k.SetValue("DSHLauncher", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\"");
                else
                    k.DeleteValue("DSHLauncher", false);
                k.Close();
                Log(on ? "Auto-start enabled" : "Auto-start disabled");
            }
            catch { }
        }

        private bool GetAutoStart()
        {
            try
            {
                RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
                if (k != null) { object v = k.GetValue("DSHLauncher"); k.Close(); return v != null; }
            }
            catch { }
            return false;
        }

        // ---------------- 托盘 ----------------
        private SD.Icon MakeGrayIconRuntime()
        {
            try
            {
                SD.Icon src = new SD.Icon(IcoPath);
                SD.Bitmap bmp = src.ToBitmap();
                SD.Bitmap gray = new SD.Bitmap(bmp.Width, bmp.Height);
                using (SD.Graphics g = SD.Graphics.FromImage(gray))
                {
                    SD.Imaging.ColorMatrix cm = new SD.Imaging.ColorMatrix(new float[][]
                    {
                        new float[] { 0.12f, 0.12f, 0.12f, 0, 0 },
                        new float[] { 0.236f, 0.236f, 0.236f, 0, 0 },
                        new float[] { 0.044f, 0.044f, 0.044f, 0, 0 },
                        new float[] { 0, 0, 0, 1, 0 },
                        new float[] { 0, 0, 0, 0, 1 }
                    });
                    SD.Imaging.ImageAttributes ia = new SD.Imaging.ImageAttributes();
                    ia.SetColorMatrix(cm);
                    g.DrawImage(bmp, new SD.Rectangle(0, 0, bmp.Width, bmp.Height), 0, 0, bmp.Width, bmp.Height, SD.GraphicsUnit.Pixel, ia);
                }
                IntPtr hicon = gray.GetHicon();
                GrayHicon = hicon;
                SD.Icon result = SD.Icon.FromHandle(hicon);
                bmp.Dispose();
                gray.Dispose();
                src.Dispose();
                Log("Gray icon created OK");
                return result;
            }
            catch (Exception ex)
            {
                Log("Gray icon failed: " + ex.Message);
                return null;
            }
        }

        private void SetTrayStatusIcon(bool running)
        {
            try
            {
                SD.Icon target = running ? IconColor : IconGray;
                if (target == null) return;
                Tray.Visible = false;
                Tray.Icon = target;
                Tray.Visible = true;
                Log(running ? "Tray icon -> color" : "Tray icon -> gray");
            }
            catch { }
        }

        private void BuildTray()
        {
            Tray = new WF.NotifyIcon();
            try
            {
                IconColor = new SD.Icon(IcoPath);
                IconGray = MakeGrayIconRuntime();
            }
            catch (Exception ex) { Log("Tray icon init failed: " + ex.Message); }
            if (IconColor != null) Tray.Icon = IconColor;
            Tray.Text = "DeepSeek Harness 服务管理器";
            Tray.Visible = true;
            WF.ContextMenuStrip menu = new WF.ContextMenuStrip();
            menu.Items.Add("打开启动器", null, delegate(object s, EventArgs e) { ShowWindow(); });
            menu.Items.Add("打开网页", null, delegate(object s, EventArgs e) { OpenWeb(); });
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("启动服务", null, delegate(object s, EventArgs e) { StartServer(); });
            menu.Items.Add("停止服务", null, delegate(object s, EventArgs e) { StopServer(); });
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("退出", null, delegate(object s, EventArgs e) { ConfirmExit(); });
            Tray.ContextMenuStrip = menu;
            Tray.DoubleClick += delegate(object s, EventArgs e) { ShowWindow(); };
        }

        private void NotifyState(string text, WF.ToolTipIcon icon)
        {
            if (!NotifyEnabled) return;
            try { Tray.ShowBalloonTip(1500, "DeepSeek Harness", text, icon); } catch { }
        }

        private void ShowWindow()
        {
            Show();
            Activate();
            WindowState = WindowState.Normal;
            Topmost = true;
            Topmost = false;
        }

        private void HideToTray(string logMsg)
        {
            Hide();
            Log(logMsg);
            try { Tray.ShowBalloonTip(2500, "DeepSeek Harness", "启动器已转入后台（系统托盘），服务保持运行。双击托盘图标可恢复窗口。", WF.ToolTipIcon.Info); } catch { }
        }

        private void ConfirmExit()
        {
            if (PortAlive())
            {
                string r = ShowConfirm("退出 DeepSeek Harness",
                    "服务仍在运行。\n\n退出后启动器与托盘图标将关闭，服务器保持运行（网页仍可访问）。\n再次使用时双击桌面图标打开启动器即可。\n\n也可选择同时停止服务器。", "YesNoCancel");
                if (r == "Yes") { ExitApp(true); }
                else if (r == "No") { ExitApp(false); }
            }
            else
            {
                ExitApp(false);
            }
        }

        private void ExitApp(bool stopServer)
        {
            if (stopServer) { StopServer(); Log("Exit requested: server stopped"); }
            else { Log("Exit requested: server left running"); }
            ReallyExit = true;
            SavePosition();
            try
            {
                if (Tray != null) { Tray.Visible = false; Tray.Dispose(); }
                if (IconColor != null) IconColor.Dispose();
                if (IconGray != null) IconGray.Dispose();
                if (GrayHicon != IntPtr.Zero) DestroyIcon(GrayHicon);
            }
            catch { }
            Close();
        }

        // ---------------- 定时器 ----------------
        private void StartTimer()
        {
            Timer = new DispatcherTimer();
            Timer.Interval = TimeSpan.FromSeconds(1);
            Timer.Tick += delegate(object s, EventArgs e) { Tick(); };
            Timer.Start();
        }

        private void Tick()
        {
            CheckShowSignal();
            bool running = PortAlive();
            if (running != LastState)
            {
                LastState = running;
                if (running)
                {
                    RunningSince = DateTime.Now;
                    Starting = false;   // 启动完成
                    Log("State changed -> running");
                    try
                    {
                        Tray.Text = "DeepSeek Harness - 运行中";
                        SetTrayStatusIcon(true);
                    }
                    catch { }
                    NotifyState("服务已启动，网页可用。", WF.ToolTipIcon.Info);
                }
                else
                {
                    RunningSince = DateTime.MinValue;
                    Log("State changed -> stopped");
                    try
                    {
                        Tray.Text = "DeepSeek Harness - 已停止";
                        SetTrayStatusIcon(false);
                    }
                    catch { }
                    NotifyState("服务已停止。", WF.ToolTipIcon.Warning);
                    TxtCpu.Text = "--%";
                    TxtMem.Text = "-- MB";
                    TxtProc.Text = "--";
                    TxtMeta.Text = "PID -- · 端口 " + Port;
                    TxtWarn.Visibility = Visibility.Collapsed;
                    ExposureWarned = false;
                    if (AutoRestart && (DateTime.Now - LastManualStop).TotalSeconds > 30)
                    {
                        Log("Service stopped unexpectedly, auto-restarting...");
                        NotifyState("服务意外停止，正在自动重启…", WF.ToolTipIcon.Warning);
                        StartServer();
                    }
                }
            }
            if (running)
            {
                int pid = GetListenerPidCached();
                try
                {
                    Process pr = Process.GetProcessById(pid);
                    TxtProc.Text = pid.ToString();
                    double memMB = Math.Round(pr.WorkingSet64 / 1048576.0);
                    TxtMem.Text = memMB + " MB";
                    double pct = 0;
                    DateTime now = DateTime.UtcNow;
                    TimeSpan tick = pr.TotalProcessorTime;
                    if (HasCpuSample)
                    {
                        double dt = Math.Max(1, (now - LastCpuTime).TotalMilliseconds);
                        pct = (tick - LastCpuTick).TotalMilliseconds / dt * 100.0 / Environment.ProcessorCount;
                        if (pct < 0) pct = 0;
                        TxtCpu.Text = Math.Round(pct) + "%";
                    }
                    LastCpuTick = tick; LastCpuTime = now; HasCpuSample = true;
                    CpuHistory.Add(pct);
                    if (CpuHistory.Count > 60) CpuHistory.RemoveAt(0);
                    MemHistory.Add(memMB);
                    if (MemHistory.Count > 60) MemHistory.RemoveAt(0);
                    if (memMB > MemMax) MemMax = memMB;
                    UpdateTrend();
                    if (RunningSince != DateTime.MinValue)
                        TxtMeta.Text = "PID " + pid + " · 运行 " + FormatDuration(DateTime.Now - RunningSince);
                    else
                        TxtMeta.Text = "PID " + pid + " · 启动 " + pr.StartTime.ToString("HH:mm:ss");
                }
                catch
                {
                    TxtProc.Text = "--";
                }
            }
            else
            {
                HasCpuSample = false;
            }
            // 启动保护 + 失败诊断：
            // - node 进程已退出 → 立即诊断（启动失败）
            // - node 仍在运行 → 最多等 120 秒（首次启动/冷启动较慢，不误报）
            if (Starting && !running)
            {
                bool nodeGone = (ServerProc == null || ServerProc.HasExited);
                double elapsed = (DateTime.Now - StartingSince).TotalSeconds;
                if (nodeGone || elapsed > 120)
                {
                    Log("Start diagnosis: nodeGone=" + nodeGone + " elapsed=" + Math.Round(elapsed) + "s");
                    Starting = false;
                    StringBuilder diag = new StringBuilder();
                    if (nodeGone)
                        diag.AppendLine("服务进程已退出，启动失败。可能原因：");
                    else
                        diag.AppendLine("服务在 120 秒内未就绪，可能原因：");
                    bool nodeAlive = (ServerProc != null && !ServerProc.HasExited);
                    diag.AppendLine(nodeAlive ? "· node 进程仍在运行（可能启动缓慢或配置问题）" : "· node 进程已退出");
                if (File.Exists(ServerErr))
                {
                    string[] errLines = ReadTail(ServerErr, 6);
                    bool hasErr = false;
                    foreach (string el in errLines) { if (el.Trim().Length > 0) hasErr = true; }
                    if (hasErr)
                    {
                        diag.AppendLine("· 错误输出：");
                        foreach (string el in errLines) diag.AppendLine("    " + el);
                    }
                }
                diag.AppendLine("请查看「运行日志」的 [server stderr] 部分排查。");
                try { ShowConfirm("服务启动超时", diag.ToString(), "OK"); } catch { }
                }
            }
            UpdateStatusUi(running);
            SysCheckCounter++;
            if (FollowSystem && SysCheckCounter % 20 == 0)
            {
                bool sys = Theme.GetSystemLight();
                if (sys != SystemLightTheme)
                {
                    SystemLightTheme = sys;
                    Theme.Light = sys;
                    ApplyTheme();
                }
            }
            if (running && SysCheckCounter % 10 == 0)
            {
                string addr = GetListenAddressCached();
                bool exposed = IsExposedAddress(addr);
                if (exposed != ExposureWarned)
                {
                    ExposureWarned = exposed;
                    if (exposed)
                    {
                        TxtWarn.Text = "⚠ 服务监听在 " + addr + "，已暴露在局域网！";
                        TxtWarn.Visibility = Visibility.Visible;
                        Log("SECURITY: service listening on " + addr + " (EXPOSED to network)");
                        NotifyState("安全警告：服务监听在 " + addr + "，已暴露在局域网", WF.ToolTipIcon.Warning);
                    }
                    else
                    {
                        TxtWarn.Visibility = Visibility.Collapsed;
                        Log("SECURITY: service listening on " + addr + " (safe)");
                    }
                }
            }
            UpdateLog();
        }

        private void CheckShowSignal()
        {
            try
            {
                if (File.Exists(ShowFile))
                {
                    File.Delete(ShowFile);
                    ShowWindow();
                    Log("Activated by second launch");
                }
            }
            catch { }
        }

        // 统一更新状态点/状态文字/启停按钮（含"启动中"状态）
        private void UpdateStatusUi(bool running)
        {
            TextBlock t = (TextBlock)BtnToggle.Child;
            if (Starting)
            {
                DotStatus.Fill = Theme.Brush("Amber");
                TxtStatus.Text = "启动中…";
                TxtStatus.Foreground = Theme.Brush("Amber");
                BtnToggle.Background = Theme.Brush("Accent");
                t.Text = "启动中…";
                t.Foreground = Brushes.White;
                BtnToggle.Opacity = 0.6;
            }
            else if (running)
            {
                DotStatus.Fill = Theme.Brush("Green");
                TxtStatus.Text = "运行中";
                TxtStatus.Foreground = Theme.Brush("Green");
                BtnToggle.Background = Theme.Brush("Red");
                t.Text = "停止服务";
                t.Foreground = Brushes.White;
                BtnToggle.Opacity = 1.0;
            }
            else
            {
                DotStatus.Fill = Theme.Brush("FgMuted");
                TxtStatus.Text = "已停止";
                TxtStatus.Foreground = Theme.Brush("FgSecondary");
                BtnToggle.Background = Theme.Brush("Accent");
                t.Text = "启动服务";
                t.Foreground = Brushes.White;
                BtnToggle.Opacity = 1.0;
            }
        }

        // ---------------- 趋势图 ----------------
        private void UpdateTrend()
        {
            if (TrendCanvas == null) return;
            TrendCanvas.Children.Clear();
            double w = TrendCanvas.ActualWidth;
            double h = TrendCanvas.ActualHeight;
            if (w <= 10 || h <= 10) return;
            for (int i = 1; i <= 3; i++)
            {
                Line ln = new Line();
                ln.X1 = 0; ln.X2 = w;
                ln.Y1 = h * i / 4.0; ln.Y2 = h * i / 4.0;
                ln.Stroke = Theme.Brush("BorderW");
                ln.StrokeThickness = 0.5;
                TrendCanvas.Children.Add(ln);
            }
            if (CpuHistory.Count > 1)
                DrawTrendLine(CpuHistory, w, h, 100.0, Theme.Brush("Accent"));
            if (MemHistory.Count > 1)
                DrawTrendLine(MemHistory, w, h, MemMax, Theme.Brush("Green"));
            if (TrendLegend != null)
            {
                string cpuTxt = CpuHistory.Count > 0 ? Math.Round(CpuHistory[CpuHistory.Count - 1]) + "%" : "--";
                string memTxt = MemHistory.Count > 0 ? Math.Round(MemHistory[MemHistory.Count - 1]) + " MB" : "--";
                TrendLegend.Text = "CPU " + cpuTxt + "  内存 " + memTxt;
            }
        }

        private void DrawTrendLine(List<double> data, double w, double h, double maxVal, Brush brush)
        {
            PointCollection pts = new PointCollection();
            int n = data.Count;
            double step = w / 59.0;
            for (int i = 0; i < n; i++)
            {
                double x = w - (n - 1 - i) * step;
                double v = data[i] / maxVal;
                if (v < 0) v = 0;
                if (v > 1) v = 1;
                double y = h - v * (h - 6) - 3;
                pts.Add(new Point(x, y));
            }
            Polyline pl = new Polyline();
            pl.Points = pts;
            pl.Stroke = brush;
            pl.StrokeThickness = 1.5;
            pl.StrokeLineJoin = PenLineJoin.Round;
            TrendCanvas.Children.Add(pl);
        }

        private string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalDays >= 1) return (int)ts.TotalDays + "天" + ts.Hours + "时";
            if (ts.TotalHours >= 1) return (int)ts.TotalHours + "时" + ts.Minutes + "分";
            if (ts.TotalMinutes >= 1) return (int)ts.TotalMinutes + "分" + ts.Seconds + "秒";
            return (int)ts.TotalSeconds + "秒";
        }

        // ---------------- 日志 ----------------
        private void UpdateLog()
        {
            DateTime m1 = File.Exists(LauncherLog) ? File.GetLastWriteTime(LauncherLog) : DateTime.MinValue;
            DateTime m2 = File.Exists(ServerOut) ? File.GetLastWriteTime(ServerOut) : DateTime.MinValue;
            DateTime m3 = File.Exists(ServerErr) ? File.GetLastWriteTime(ServerErr) : DateTime.MinValue;
            if (m1 == LogMTime1 && m2 == LogMTime2 && m3 == LogMTime3 && LastLogText != "") return;
            LogMTime1 = m1; LogMTime2 = m2; LogMTime3 = m3;

            List<string> lines = new List<string>();
            try
            {
                if (File.Exists(LauncherLog))
                {
                    string[] l = ReadTail(LauncherLog, 25);
                    foreach (string x in l) lines.Add(x);
                }
                lines.Add("");
                lines.Add("[server stdout]");
                if (File.Exists(ServerOut))
                {
                    string[] l = ReadTail(ServerOut, 90);
                    foreach (string x in l) lines.Add(x);
                }
                lines.Add("");
                lines.Add("[server stderr]");
                if (File.Exists(ServerErr))
                {
                    string[] l = ReadTail(ServerErr, 30);
                    foreach (string x in l) lines.Add(x);
                }
            }
            catch { }
            string newText = string.Join("\n", lines.ToArray());
            if (newText == LastLogText) return;
            LastLogText = newText;

            string kw = TxtHighlight.Text.Trim();
            SolidColorBrush bDefault = Theme.Brush("FgLog");
            SolidColorBrush bRed = Theme.Brush("Red");
            SolidColorBrush bAmber = Theme.Brush("Amber");
            SolidColorBrush bBlue = Theme.Brush("LinkBlue");
            SolidColorBrush bHi = Theme.Brush("HiBg");

            FlowDocument doc = new FlowDocument();
            doc.PagePadding = new Thickness(8, 4, 8, 6);
            foreach (string line in lines)
            {
                Paragraph p = new Paragraph();
                p.Margin = new Thickness(0);
                Run run = new Run(line);
                run.Foreground = bDefault;
                if (Regex.IsMatch(line, "(?i)error|错误|exception|failed")) run.Foreground = bRed;
                else if (Regex.IsMatch(line, "(?i)warn|警告|deprecated")) run.Foreground = bAmber;
                else if (line.IndexOf("[launcher]") >= 0) run.Foreground = bBlue;
                if (kw.Length > 0 && line.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    p.Background = bHi;
                    run.FontWeight = FontWeights.SemiBold;
                }
                p.Inlines.Add(run);
                doc.Blocks.Add(p);
            }
            TxtLog.Document = doc;
            TxtLog.ScrollToEnd();
        }

        private string[] ReadTail(string path, int count)
        {
            List<string> result = new List<string>();
            try
            {
                using (StreamReader r = new StreamReader(path, Encoding.UTF8))
                {
                    string line;
                    List<string> buf = new List<string>();
                    while ((line = r.ReadLine()) != null)
                    {
                        buf.Add(line);
                        if (buf.Count > count) buf.RemoveAt(0);
                    }
                    result = buf;
                }
            }
            catch { }
            return result.ToArray();
        }

        // ---------------- 对话框 ----------------
        private string DlgResult = null;

        private string ShowConfirm(string title, string message, string buttons, string btn1Text = null, string btn2Text = null, string btn3Text = null)
        {
            Window dlg = new Window();
            dlg.Width = 400;
            dlg.SizeToContent = SizeToContent.Height;
            dlg.WindowStyle = WindowStyle.None;
            dlg.AllowsTransparency = true;
            dlg.Background = Brushes.Transparent;
            dlg.ResizeMode = ResizeMode.NoResize;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dlg.ShowInTaskbar = false;
            dlg.FontFamily = new FontFamily(Theme.FontFamily);
            try { dlg.Owner = this; } catch { }

            Border dBorder = new Border();
            dBorder.Margin = new Thickness(8);
            dBorder.CornerRadius = new CornerRadius(12);
            dBorder.BorderThickness = new Thickness(1);
            dBorder.Background = Theme.Brush("BgWindow");
            dBorder.BorderBrush = Theme.Brush("BorderW");
            dBorder.Effect = new DropShadowEffect() { Color = Colors.Black, BlurRadius = 20, ShadowDepth = 4, Opacity = 0.5 };
            dlg.Content = dBorder;

            Grid dGrid = new Grid();
            dGrid.Margin = new Thickness(20, 16, 20, 18);
            dBorder.Child = dGrid;
            dGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            dGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            dGrid.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });

            Grid head = new Grid();
            StackPanel headL = new StackPanel();
            headL.Orientation = Orientation.Horizontal;
            Ellipse dDot = new Ellipse();
            dDot.Width = 10; dDot.Height = 10;
            dDot.Fill = Theme.Brush("Accent");
            dDot.VerticalAlignment = VerticalAlignment.Center;
            headL.Children.Add(dDot);
            TextBlock dTitle = new TextBlock();
            dTitle.Text = title;
            dTitle.FontSize = 14;
            dTitle.FontWeight = FontWeights.SemiBold;
            dTitle.Margin = new Thickness(9, 0, 0, 0);
            dTitle.Foreground = Theme.Brush("FgPrimary");
            headL.Children.Add(dTitle);
            head.Children.Add(headL);
            TextBlock dX = new TextBlock();
            dX.Text = "✕";
            dX.Width = 26; dX.Height = 26;
            dX.TextAlignment = TextAlignment.Center;
            dX.HorizontalAlignment = HorizontalAlignment.Right;
            dX.Foreground = Theme.Brush("FgSecondary");
            dX.Cursor = Cursors.Hand;
            dX.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { DlgResult = "Cancel"; dlg.Close(); };
            head.Children.Add(dX);
            dGrid.Children.Add(head);

            TextBlock dMsg = new TextBlock();
            dMsg.Text = message;
            dMsg.FontSize = 12.5;
            dMsg.Foreground = Theme.Brush("FgSecondary");
            dMsg.TextWrapping = TextWrapping.Wrap;
            dMsg.Margin = new Thickness(0, 14, 0, 0);
            Grid.SetRow(dMsg, 1);
            dGrid.Children.Add(dMsg);

            StackPanel dBtns = new StackPanel();
            dBtns.Orientation = Orientation.Horizontal;
            dBtns.HorizontalAlignment = HorizontalAlignment.Right;
            dBtns.Margin = new Thickness(0, 20, 0, 0);
            Grid.SetRow(dBtns, 2);
            dGrid.Children.Add(dBtns);

            bool yesNoCancel = (buttons == "YesNoCancel");
            string b1Text = btn1Text != null ? btn1Text : (yesNoCancel ? "是" : (buttons == "OK" ? "确定" : "是"));
            string b2Text = btn2Text != null ? btn2Text : "否";
            string b3Text = btn3Text != null ? btn3Text : "取消";
            Border b1 = MakeDialogButton(b1Text, "Accent", delegate() { DlgResult = yesNoCancel ? "Yes" : (buttons == "OK" ? "OK" : "Yes"); dlg.Close(); });
            b1.Margin = new Thickness(0, 0, 8, 0);
            dBtns.Children.Add(b1);
            if (yesNoCancel || buttons == "YesNo")
            {
                Border b2 = MakeDialogButton(b2Text, "BgCardAlt", delegate() { DlgResult = "No"; dlg.Close(); });
                b2.Margin = new Thickness(0, 0, 8, 0);
                dBtns.Children.Add(b2);
            }
            if (yesNoCancel)
            {
                Border b3 = MakeDialogButton(b3Text, "BgCardAlt", delegate() { DlgResult = "Cancel"; dlg.Close(); });
                b3.Margin = new Thickness(0, 0, 8, 0);
                dBtns.Children.Add(b3);
            }

            DlgResult = null;
            dlg.ShowDialog();
            return DlgResult;
        }

        private Border MakeDialogButton(string text, string bgKey, Action click)
        {
            Border b = new Border();
            b.Width = 96; b.Height = 32;
            b.CornerRadius = new CornerRadius(8);
            b.Cursor = Cursors.Hand;
            b.Background = Theme.Brush(bgKey);
            TextBlock t = new TextBlock();
            t.Text = text;
            t.FontSize = 12;
            t.FontWeight = FontWeights.SemiBold;
            t.Foreground = Brushes.White;
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            b.Child = t;
            b.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { click(); };
            b.MouseEnter += delegate(object s, MouseEventArgs e) { b.Opacity = 0.85; };
            b.MouseLeave += delegate(object s, MouseEventArgs e) { b.Opacity = 1.0; };
            return b;
        }

        private Border MakeSettingToggle(bool initial, Action<bool> onChange)
        {
            bool sw = initial;
            Border track = new Border();
            track.Width = 36; track.Height = 20;
            track.CornerRadius = new CornerRadius(10);
            track.Cursor = Cursors.Hand;
            Ellipse thumb = new Ellipse();
            thumb.Width = 14; thumb.Height = 14;
            thumb.Fill = Brushes.White;
            thumb.HorizontalAlignment = HorizontalAlignment.Left;
            thumb.Margin = new Thickness(3, 0, 0, 0);
            track.Child = thumb;
            Action swApply = delegate()
            {
                track.Background = Theme.Brush(sw ? "Accent" : "ToggleOff");
                thumb.HorizontalAlignment = sw ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                thumb.Margin = sw ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
            };
            swApply();
            track.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                sw = !sw;
                swApply();
                onChange(sw);
            };
            return track;
        }

        private void UpdatePortUi()
        {
            try
            {
                TxtUrl.Text = "http://127.0.0.1:" + Port;
                TxtMeta.Text = "PID -- · 端口 " + Port;
                TxtPort.Text = Port.ToString();
            }
            catch { }
        }

        // ---------------- 设置窗口 ----------------
        private void ShowSettingsDialog()
        {
            Window dlg = new Window();
            dlg.Width = 420;
            dlg.SizeToContent = SizeToContent.Height;
            dlg.WindowStyle = WindowStyle.None;
            dlg.AllowsTransparency = true;
            dlg.Background = Brushes.Transparent;
            dlg.ResizeMode = ResizeMode.NoResize;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dlg.ShowInTaskbar = false;
            dlg.FontFamily = new FontFamily(Theme.FontFamily);
            try { dlg.Owner = this; } catch { }

            Border dBorder = new Border();
            dBorder.Margin = new Thickness(8);
            dBorder.CornerRadius = new CornerRadius(12);
            dBorder.BorderThickness = new Thickness(1);
            dBorder.Background = Theme.Brush("BgWindow");
            dBorder.BorderBrush = Theme.Brush("BorderW");
            dBorder.Effect = new DropShadowEffect() { Color = Colors.Black, BlurRadius = 20, ShadowDepth = 4, Opacity = 0.5 };
            dlg.Content = dBorder;

            Grid g = new Grid();
            g.Margin = new Thickness(20, 16, 20, 18);
            dBorder.Child = g;
            for (int i = 0; i < 8; i++)
                g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });

            Grid head = new Grid();
            TextBlock title = new TextBlock();
            title.Text = "设置";
            title.FontSize = 14; title.FontWeight = FontWeights.SemiBold;
            title.Foreground = Theme.Brush("FgPrimary");
            head.Children.Add(title);
            TextBlock closeX = new TextBlock();
            closeX.Text = "✕";
            closeX.Width = 26; closeX.Height = 26;
            closeX.TextAlignment = TextAlignment.Center;
            closeX.HorizontalAlignment = HorizontalAlignment.Right;
            closeX.Foreground = Theme.Brush("FgSecondary");
            closeX.Cursor = Cursors.Hand;
            closeX.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { dlg.Close(); };
            head.Children.Add(closeX);
            g.Children.Add(head);

            // 分组 1: dsh 路径
            Border card1 = new Border();
            card1.Margin = new Thickness(0, 14, 0, 0);
            card1.CornerRadius = new CornerRadius(10);
            card1.Background = Theme.Brush("BgCard");
            card1.Padding = new Thickness(14, 12, 14, 12);
            Grid.SetRow(card1, 1);
            g.Children.Add(card1);
            Grid c1g = new Grid();
            card1.Child = c1g;
            c1g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            c1g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            c1g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            TextBlock c1t = new TextBlock();
            c1t.Text = "dsh 程序路径";
            c1t.FontSize = 12; c1t.FontWeight = FontWeights.SemiBold;
            c1t.Foreground = Theme.Brush("FgPrimary");
            c1g.Children.Add(c1t);
            TextBox pathBox = new TextBox();
            pathBox.Text = (BinPath != null ? BinPath : "(未找到，请手动指定)");
            pathBox.IsReadOnly = true;
            pathBox.Height = 30;
            pathBox.FontSize = 10.5;
            pathBox.VerticalContentAlignment = VerticalAlignment.Center;
            pathBox.Padding = new Thickness(8, 0, 8, 0);
            pathBox.Margin = new Thickness(0, 10, 0, 0);
            pathBox.Background = Theme.Brush("BgLog");
            pathBox.Foreground = Theme.Brush("FgLog");
            pathBox.BorderBrush = Theme.Brush("BorderW");
            pathBox.BorderThickness = new Thickness(1);
            Grid.SetRow(pathBox, 1);
            c1g.Children.Add(pathBox);
            Border btnChange = MakeDialogButton("更改路径", "BgCardAlt", delegate()
            {
                string r = ShowPathDialog();
                if (r != null)
                {
                    BinPath = r;
                    Settings.SetString("binpath", r);
                    Log("SECURITY: bin path set: " + r);
                    pathBox.Text = r;
                }
            });
            btnChange.Width = 84; btnChange.Height = 28;
            btnChange.HorizontalAlignment = HorizontalAlignment.Right;
            btnChange.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(btnChange, 2);
            c1g.Children.Add(btnChange);

            // 分组 2: 通用
            Border card2 = new Border();
            card2.Margin = new Thickness(0, 10, 0, 0);
            card2.CornerRadius = new CornerRadius(10);
            card2.Background = Theme.Brush("BgCard");
            card2.Padding = new Thickness(14, 10, 14, 10);
            Grid.SetRow(card2, 2);
            g.Children.Add(card2);
            StackPanel c2row = new StackPanel();
            c2row.Orientation = Orientation.Horizontal;
            StackPanel c2wrap = new StackPanel();
            card2.Child = c2wrap;
            c2wrap.Children.Add(c2row);
            bool sw = AutoStartChecked;
            Border swTrack = new Border();
            swTrack.Width = 36; swTrack.Height = 20;
            swTrack.CornerRadius = new CornerRadius(10);
            swTrack.Cursor = Cursors.Hand;
            Ellipse swThumb = new Ellipse();
            swThumb.Width = 14; swThumb.Height = 14;
            swThumb.Fill = Brushes.White;
            swThumb.HorizontalAlignment = HorizontalAlignment.Left;
            swThumb.Margin = new Thickness(3, 0, 0, 0);
            swTrack.Child = swThumb;
            Action swApply = delegate()
            {
                swTrack.Background = Theme.Brush(sw ? "Accent" : "ToggleOff");
                swThumb.HorizontalAlignment = sw ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                swThumb.Margin = sw ? new Thickness(0, 0, 3, 0) : new Thickness(3, 0, 0, 0);
            };
            swApply();
            swTrack.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                sw = !sw;
                AutoStartChecked = sw;
                SetAutoStart(sw);
                swApply();
                if (AutoStartApply != null) AutoStartApply();
            };
            c2row.Children.Add(swTrack);
            TextBlock c2lb = new TextBlock();
            c2lb.Text = "开机自启";
            c2lb.FontSize = 12;
            c2lb.VerticalAlignment = VerticalAlignment.Center;
            c2lb.Margin = new Thickness(8, 0, 0, 0);
            c2lb.Foreground = Theme.Brush("FgSecondary");
            c2row.Children.Add(c2lb);
            StackPanel c2row2 = new StackPanel();
            c2row2.Orientation = Orientation.Horizontal;
            c2row2.Margin = new Thickness(0, 10, 0, 0);
            c2row2.Children.Add(MakeSettingToggle(NotifyEnabled, delegate(bool v) { NotifyEnabled = v; Settings.SetString("notify", v ? "on" : "off"); }));
            TextBlock c2lb2 = new TextBlock();
            c2lb2.Text = "状态变化通知";
            c2lb2.FontSize = 12;
            c2lb2.VerticalAlignment = VerticalAlignment.Center;
            c2lb2.Margin = new Thickness(8, 0, 0, 0);
            c2lb2.Foreground = Theme.Brush("FgSecondary");
            c2row2.Children.Add(c2lb2);
            c2wrap.Children.Add(c2row2);

            // 分组 3: 服务设置
            Border card3 = new Border();
            card3.Margin = new Thickness(0, 10, 0, 0);
            card3.CornerRadius = new CornerRadius(10);
            card3.Background = Theme.Brush("BgCard");
            card3.Padding = new Thickness(14, 10, 14, 10);
            Grid.SetRow(card3, 3);
            g.Children.Add(card3);
            StackPanel c3 = new StackPanel();
            card3.Child = c3;
            TextBlock c3t = new TextBlock();
            c3t.Text = "服务";
            c3t.FontSize = 12; c3t.FontWeight = FontWeights.SemiBold;
            c3t.Foreground = Theme.Brush("FgPrimary");
            c3.Children.Add(c3t);
            StackPanel rowPort = new StackPanel();
            rowPort.Orientation = Orientation.Horizontal;
            rowPort.Margin = new Thickness(0, 10, 0, 0);
            TextBlock portLb = new TextBlock();
            portLb.Text = "端口";
            portLb.FontSize = 12;
            portLb.VerticalAlignment = VerticalAlignment.Center;
            portLb.Foreground = Theme.Brush("FgSecondary");
            rowPort.Children.Add(portLb);
            TextBox portBox = new TextBox();
            portBox.Text = Port.ToString();
            portBox.Width = 70; portBox.Height = 26;
            portBox.FontSize = 12;
            portBox.VerticalContentAlignment = VerticalAlignment.Center;
            portBox.Margin = new Thickness(10, 0, 0, 0);
            portBox.Background = Theme.Brush("BgLog");
            portBox.Foreground = Theme.Brush("FgPrimary");
            portBox.CaretBrush = Theme.Brush("FgPrimary");
            portBox.BorderBrush = Theme.Brush("BorderW");
            portBox.BorderThickness = new Thickness(1);
            rowPort.Children.Add(portBox);
            Border btnPort = MakeDialogButton("应用", "BgCardAlt", delegate()
            {
                int p;
                if (int.TryParse(portBox.Text.Trim(), out p) && p > 0 && p < 65536)
                {
                    if (p != Port)
                    {
                        string r = ShowConfirm("修改端口", "确定将端口从 " + Port + " 改为 " + p + "？\n服务需重启后生效。", "YesNo");
                        if (r != "Yes") return;
                        int oldPort = Port;
                        Port = p;
                        Settings.SetInt("port", p);
                        UpdatePortUi();
                        Log("SECURITY: Port changed from " + oldPort + " to " + p);
                        TxtFooter.Text = "端口已改为 " + p + "（重启服务生效）";
                        DispatcherTimer t = new DispatcherTimer();
                        t.Interval = TimeSpan.FromMilliseconds(2000);
                        t.Tick += delegate(object s, EventArgs e) { TxtFooter.Text = FooterVer; t.Stop(); };
                        t.Start();
                    }
                }
                else
                {
                    portBox.Text = Port.ToString();
                }
            });
            btnPort.Width = 60; btnPort.Height = 26;
            btnPort.Margin = new Thickness(10, 0, 0, 0);
            rowPort.Children.Add(btnPort);
            c3.Children.Add(rowPort);
            StackPanel rowAr = new StackPanel();
            rowAr.Orientation = Orientation.Horizontal;
            rowAr.Margin = new Thickness(0, 10, 0, 0);
            rowAr.Children.Add(MakeSettingToggle(AutoRestart, delegate(bool v) { AutoRestart = v; Settings.SetString("autoRestart", v ? "on" : "off"); }));
            TextBlock arLb = new TextBlock();
            arLb.Text = "服务异常自动重启";
            arLb.FontSize = 12;
            arLb.VerticalAlignment = VerticalAlignment.Center;
            arLb.Margin = new Thickness(8, 0, 0, 0);
            arLb.Foreground = Theme.Brush("FgSecondary");
            rowAr.Children.Add(arLb);
            c3.Children.Add(rowAr);
            StackPanel rowAw = new StackPanel();
            rowAw.Orientation = Orientation.Horizontal;
            rowAw.Margin = new Thickness(0, 10, 0, 0);
            rowAw.Children.Add(MakeSettingToggle(AutoOpenWeb, delegate(bool v) { AutoOpenWeb = v; Settings.SetString("autoOpenWeb", v ? "on" : "off"); }));
            TextBlock awLb = new TextBlock();
            awLb.Text = "启动服务后自动打开网页";
            awLb.FontSize = 12;
            awLb.VerticalAlignment = VerticalAlignment.Center;
            awLb.Margin = new Thickness(8, 0, 0, 0);
            awLb.Foreground = Theme.Brush("FgSecondary");
            rowAw.Children.Add(awLb);
            c3.Children.Add(rowAw);
            StackPanel rowAm = new StackPanel();
            rowAm.Orientation = Orientation.Horizontal;
            rowAm.Margin = new Thickness(0, 10, 0, 0);
            rowAm.Children.Add(MakeSettingToggle(AppModeOpen, delegate(bool v) { AppModeOpen = v; Settings.SetString("appMode", v ? "on" : "off"); }));
            TextBlock amLb = new TextBlock();
            amLb.Text = "在独立应用窗口打开网页（Chrome 应用模式）";
            amLb.FontSize = 12;
            amLb.VerticalAlignment = VerticalAlignment.Center;
            amLb.Margin = new Thickness(8, 0, 0, 0);
            amLb.Foreground = Theme.Brush("FgSecondary");
            rowAm.Children.Add(amLb);
            c3.Children.Add(rowAm);

            // 分组 4: 外观
            Border card4 = new Border();
            card4.Margin = new Thickness(0, 10, 0, 0);
            card4.CornerRadius = new CornerRadius(10);
            card4.Background = Theme.Brush("BgCard");
            card4.Padding = new Thickness(14, 10, 14, 10);
            Grid.SetRow(card4, 4);
            g.Children.Add(card4);
            StackPanel c4row = new StackPanel();
            c4row.Orientation = Orientation.Horizontal;
            card4.Child = c4row;
            c4row.Children.Add(MakeSettingToggle(FollowSystem, delegate(bool v)
            {
                FollowSystem = v;
                Settings.SetString("followSystem", v ? "on" : "off");
                if (v)
                {
                    Theme.Light = Theme.GetSystemLight();
                    ApplyTheme();
                }
            }));
            TextBlock c4lb = new TextBlock();
            c4lb.Text = "跟随系统深浅色";
            c4lb.FontSize = 12;
            c4lb.VerticalAlignment = VerticalAlignment.Center;
            c4lb.Margin = new Thickness(8, 0, 0, 0);
            c4lb.Foreground = Theme.Brush("FgSecondary");
            c4row.Children.Add(c4lb);

            // 分组 5: 更新
            Border cardUpd = new Border();
            cardUpd.Margin = new Thickness(0, 10, 0, 0);
            cardUpd.CornerRadius = new CornerRadius(10);
            cardUpd.Background = Theme.Brush("BgCard");
            cardUpd.Padding = new Thickness(14, 10, 14, 10);
            Grid.SetRow(cardUpd, 5);
            g.Children.Add(cardUpd);
            StackPanel cupd = new StackPanel();
            cardUpd.Child = cupd;
            TextBlock cupdT = new TextBlock();
            cupdT.Text = "更新";
            cupdT.FontSize = 12; cupdT.FontWeight = FontWeights.SemiBold;
            cupdT.Foreground = Theme.Brush("FgPrimary");
            cupd.Children.Add(cupdT);
            TextBlock cupdVer = new TextBlock();
            cupdVer.Text = "当前 dsh 版本: " + (FooterVer.Length > 0 ? FooterVer : "未知");
            cupdVer.FontSize = 11;
            cupdVer.Margin = new Thickness(0, 6, 0, 0);
            cupdVer.Foreground = Theme.Brush("FgMuted");
            cupd.Children.Add(cupdVer);
            BtnUpdateBtn = MakeDialogButton(UpdateAvailable ? "发现新版本！" : "检查更新",
                UpdateAvailable ? "Accent" : "BgCardAlt",
                delegate() { dlg.Close(); CheckUpdate(); });
            BtnUpdateBtn.Width = 104; BtnUpdateBtn.Height = 28;
            BtnUpdateBtn.HorizontalAlignment = HorizontalAlignment.Right;
            BtnUpdateBtn.Margin = new Thickness(0, 10, 0, 0);
            cupd.Children.Add(BtnUpdateBtn);

            // 分组 6: 卸载（按钮文字明确，避免误解）
            Border card5 = new Border();
            card5.Margin = new Thickness(0, 10, 0, 0);
            card5.CornerRadius = new CornerRadius(10);
            card5.Background = Theme.Brush("BgCard");
            card5.Padding = new Thickness(14, 10, 14, 10);
            Grid.SetRow(card5, 6);
            g.Children.Add(card5);
            StackPanel c5 = new StackPanel();
            card5.Child = c5;
            TextBlock c5t = new TextBlock();
            c5t.Text = "卸载";
            c5t.FontSize = 12; c5t.FontWeight = FontWeights.SemiBold;
            c5t.Foreground = Theme.Brush("Red");
            c5.Children.Add(c5t);
            TextBlock c5desc = new TextBlock();
            c5desc.Text = "卸载启动器：停止服务并删除程序与数据";
            c5desc.FontSize = 11;
            c5desc.Margin = new Thickness(0, 6, 0, 0);
            c5desc.Foreground = Theme.Brush("FgMuted");
            c5.Children.Add(c5desc);
            Border btnUninstall = MakeDialogButton("卸载", "Red", delegate() { UninstallApp(); });
            btnUninstall.Width = 84; btnUninstall.Height = 28;
            btnUninstall.HorizontalAlignment = HorizontalAlignment.Right;
            btnUninstall.Margin = new Thickness(0, 10, 0, 0);
            c5.Children.Add(btnUninstall);

            Grid foot = new Grid();
            foot.Margin = new Thickness(0, 14, 0, 0);
            Grid.SetRow(foot, 7);
            g.Children.Add(foot);
            Border btnDone = MakeDialogButton("完成", "Accent", delegate() { dlg.Close(); });
            btnDone.HorizontalAlignment = HorizontalAlignment.Right;
            foot.Children.Add(btnDone);

            dlg.ShowDialog();
        }

        // ---------------- 路径设置对话框 ----------------
        private string ShowPathDialog()
        {
            Window dlg = new Window();
            dlg.Width = 440;
            dlg.SizeToContent = SizeToContent.Height;
            dlg.WindowStyle = WindowStyle.None;
            dlg.AllowsTransparency = true;
            dlg.Background = Brushes.Transparent;
            dlg.ResizeMode = ResizeMode.NoResize;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dlg.ShowInTaskbar = false;
            dlg.FontFamily = new FontFamily(Theme.FontFamily);
            try { dlg.Owner = this; } catch { }

            Border dBorder = new Border();
            dBorder.Margin = new Thickness(8);
            dBorder.CornerRadius = new CornerRadius(12);
            dBorder.BorderThickness = new Thickness(1);
            dBorder.Background = Theme.Brush("BgWindow");
            dBorder.BorderBrush = Theme.Brush("BorderW");
            dBorder.Effect = new DropShadowEffect() { Color = Colors.Black, BlurRadius = 20, ShadowDepth = 4, Opacity = 0.5 };
            dlg.Content = dBorder;

            Grid g = new Grid();
            g.Margin = new Thickness(20, 16, 20, 18);
            dBorder.Child = g;
            g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });

            TextBlock t1 = new TextBlock();
            t1.Text = "设置 dsh 程序路径 (bin.js)";
            t1.FontSize = 14; t1.FontWeight = FontWeights.SemiBold;
            t1.Foreground = Theme.Brush("FgPrimary");
            g.Children.Add(t1);

            TextBox tb = new TextBox();
            tb.Text = (BinPath != null ? BinPath : "");
            tb.Height = 28;
            tb.FontSize = 11.5;
            tb.VerticalContentAlignment = VerticalAlignment.Center;
            tb.Margin = new Thickness(0, 14, 0, 0);
            tb.Foreground = Theme.Brush("FgPrimary");
            tb.CaretBrush = Theme.Brush("FgPrimary");
            tb.Background = Theme.Brush("BgLog");
            tb.BorderBrush = Theme.Brush("BorderW");
            Grid.SetRow(tb, 1);
            g.Children.Add(tb);

            StackPanel btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            btns.HorizontalAlignment = HorizontalAlignment.Right;
            btns.Margin = new Thickness(0, 18, 0, 0);
            Grid.SetRow(btns, 2);
            g.Children.Add(btns);
            string result = null;
            Border okB = MakeDialogButton("确定", "Accent", delegate() { result = tb.Text.Trim(); dlg.Close(); });
            okB.Margin = new Thickness(0, 0, 8, 0);
            btns.Children.Add(okB);
            Border ccB = MakeDialogButton("取消", "BgCardAlt", delegate() { dlg.Close(); });
            btns.Children.Add(ccB);

            dlg.ShowDialog();
            return result;
        }

        // ---------------- 卸载（按钮文字明确，防误解） ----------------
        private void UninstallApp()
        {
            string msg = "确定要卸载 DeepSeek Harness 启动器？\n\n" +
                         "将停止服务，删除启动器程序、日志与桌面快捷方式。\n\n" +
                         "用户设置（端口/主题/路径等）：";
            string r = ShowConfirm("卸载 DeepSeek Harness", msg, "YesNoCancel",
                "卸载并删除设置", "卸载并保留设置", "取消");
            if (r == null || r == "Cancel") return;
            bool keepSettings = (r == "No");

            Log("Uninstall: start, keepSettings=" + keepSettings);

            StopServer();
            Log("Uninstall: server stopped");

            try
            {
                string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DeepSeek Harness.lnk");
                if (File.Exists(lnk)) File.Delete(lnk);
                Log("Uninstall: shortcut removed");
            }
            catch (Exception ex) { Log("Uninstall: shortcut error " + ex.Message); }

            try
            {
                RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (run != null) { run.DeleteValue("DSHLauncher", false); run.Close(); }
                Log("Uninstall: autostart removed");
            }
            catch { }

            if (!keepSettings)
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\DeepSeekHarness", false); Log("Uninstall: settings removed"); }
                catch { }
            }
            // 删除控制面板卸载注册表条目
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DeepSeekHarnessLauncher", false); Log("Uninstall: control-panel entry removed"); }
            catch { }

            try
            {
                string script = Path.Combine(Path.GetTempPath(), "dsh-uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".bat");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("ping 127.0.0.1 -n 3 >nul");
                sb.AppendLine("del /f /q \"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\"");
                sb.AppendLine("rmdir /s /q \"" + AppDir + "\"");
                sb.AppendLine("del \"%~f0\"");
                File.WriteAllText(script, sb.ToString(), Encoding.Default);
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c call \"" + script + "\"");
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.CreateNoWindow = true;
                Process.Start(psi);
                Log("Uninstall: cleanup script launched");
            }
            catch (Exception ex) { Log("Uninstall: script error " + ex.Message); }

            ReallyExit = true;
            SkipSaveSettings = true;   // 防止退出时重建设置
            try
            {
                if (Tray != null) { Tray.Visible = false; Tray.Dispose(); }
                if (IconColor != null) IconColor.Dispose();
                if (IconGray != null) IconGray.Dispose();
                if (GrayHicon != IntPtr.Zero) DestroyIcon(GrayHicon);
            }
            catch { }
            Close();
        }
    }

    // ---------------- 入口 ----------------
    public static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [STAThread]
        public static void Main()
        {
            // 命令行静默卸载（控制面板卸载入口调用）
            string[] cmdArgs = Environment.GetCommandLineArgs();
            bool uninstallMode = false;
            foreach (string a in cmdArgs)
            {
                if (a == "--uninstall") { uninstallMode = true; break; }
            }
            if (uninstallMode)
            {
                SilentUninstall();
                return;
            }

            try { SetProcessDpiAwarenessContext((IntPtr)(-4)); } catch { }

            Mutex single = new Mutex(false, "Local\\DSH_Launcher_SingleInstance");
            if (!single.WaitOne(0))
            {
                try
                {
                    string showFile = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), ".show");
                    File.WriteAllText(showFile, "1", Encoding.ASCII);
                }
                catch { }
                return;
            }
            try
            {
                string showFile2 = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), ".show");
                if (File.Exists(showFile2)) File.Delete(showFile2);
            }
            catch { }

            bool followSystem = (Settings.GetString("followSystem", "off") == "on");
            bool themeLight = followSystem ? Theme.GetSystemLight() : (Settings.GetString("theme", "dark") == "light");
            Theme.Light = themeLight;

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainForm win = new MainForm();
            app.Run(win);
            try { single.ReleaseMutex(); } catch { }
        }

        // 静默卸载（无界面，供控制面板/安装器调用）：停止服务、清理注册表/快捷方式、延迟删除目录
        private static void SilentUninstall()
        {
            string appDir = "";
            try { appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); } catch { }
            int port = Settings.GetInt("port", 3080);

            // 1. 停止监听该端口的服务进程
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "netstat";
                p.StartInfo.Arguments = "-ano";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                foreach (string line in output.Split('\n'))
                {
                    if (line.Contains(":" + port) && line.ToUpper().Contains("LISTENING"))
                    {
                        string[] parts = line.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 4)
                        {
                            int pid;
                            if (int.TryParse(parts[parts.Length - 1], out pid))
                            {
                                try { Process.GetProcessById(pid).Kill(); } catch { }
                            }
                        }
                    }
                }
            }
            catch { }

            // 2. 删除桌面快捷方式
            try
            {
                string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DeepSeek Harness.lnk");
                if (File.Exists(lnk)) File.Delete(lnk);
            }
            catch { }

            // 3. 删除开机自启与设置注册表
            try
            {
                RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (run != null) { run.DeleteValue("DSHLauncher", false); run.Close(); }
            }
            catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\DeepSeekHarness", false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DeepSeekHarnessLauncher", false); } catch { }

            // 4. 延迟删除安装目录（含本程序）
            try
            {
                string script = Path.Combine(Path.GetTempPath(), "dsh-uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".bat");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine("ping 127.0.0.1 -n 3 >nul");
                if (appDir != "") sb.AppendLine("rmdir /s /q \"" + appDir + "\"");
                sb.AppendLine("del \"%~f0\"");
                File.WriteAllText(script, sb.ToString(), Encoding.Default);
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c call \"" + script + "\"");
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch { }
        }
    }
}
