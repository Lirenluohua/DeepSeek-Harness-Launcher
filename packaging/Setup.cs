// ============================================================================
//  DeepSeek Harness 一键安装器 (C# WPF)
//  内嵌资源：launcher.exe / icon.ico / node.zip / dsh.zip
//  流程：欢迎(环境检测) -> 位置与选项 -> 安装(进度) -> 完成
//  编译：csc /target:winexe /codepage:65001 /resource:... Setup.cs
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using Path = System.IO.Path;

namespace DSHSetup
{
    public class SetupWindow : Window
    {
        // ---------------- 配色（浅色） ----------------
        private static SolidColorBrush Brush(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        private static readonly SolidColorBrush BgWin = Brush("#F7F8FA");
        private static readonly SolidColorBrush BgCard = Brush("#FFFFFF");
        private static readonly SolidColorBrush BgAlt = Brush("#E9ECF1");
        private static readonly SolidColorBrush BgLog = Brush("#F1F3F6");
        private static readonly SolidColorBrush FgPri = Brush("#1C1F24");
        private static readonly SolidColorBrush FgSec = Brush("#555C66");
        private static readonly SolidColorBrush FgMut = Brush("#8A9199");
        private static readonly SolidColorBrush BorderC = Brush("#14000000");
        private static readonly SolidColorBrush Accent = Brush("#3964FE");
        private static readonly SolidColorBrush Green = Brush("#16A34A");
        private static readonly SolidColorBrush Red = Brush("#DC2626");
        private static readonly SolidColorBrush Amber = Brush("#D97706");

        private string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeepSeekHarness");
        private bool DeskShortcut = true;
        private bool AutoStartOn = false;
        private int Step = 0;
        private bool LaunchNow = true;
        private string SetupLogPath = "";
        private bool Installed = false;

        // UI
        private Grid ContentGrid;
        private StackPanel[] Pages;
        private Border BtnPrev, BtnNext, BtnCancel;
        private TextBlock TxtFooter;
        private ProgressBar ProgBar;
        private TextBlock TxtProgress;
        private TextBlock TxtDoneDir;
        private TextBox TxtInstallDir;
        private Border BtnBrowse;
        private StackPanel ChkShortcut, ChkAutoStart;
        private bool[] ChkShortcutVal = new bool[1] { true };
        private bool[] ChkAutoStartVal = new bool[1] { false };
        private TextBlock LblShortcut, LblAutoStart;
        private TextBlock TxtDetectNet, TxtDetectChrome, TxtDetectNode;

        public SetupWindow()
        {
            Title = "DeepSeek Harness 安装";
            Width = 500; Height = 640;
            MinWidth = 460; MinHeight = 540;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei, PingFang SC");
            SourceInitialized += delegate(object s, EventArgs e) { EnableResize(); };

            BuildUi();
            ShowPage(0);
            RunChecks();
        }

        // ---------------- 窗口调整大小（无边框） ----------------
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

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

        private void Log(string msg)
        {
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg;
                File.AppendAllText(SetupLogPath, line + "\n", Encoding.UTF8);
            }
            catch { }
        }

        // ---------------- UI ----------------
        private Border MakeButton(string text, SolidColorBrush bg, int w, int h, Action click)
        {
            Border b = new Border();
            b.Width = w; b.Height = h;
            b.Cursor = Cursors.Hand;
            b.CornerRadius = new CornerRadius(8);
            b.Background = bg;
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
            return b;
        }

        private StackPanel MakeCheckItem(SolidColorBrush dot, string label, out TextBlock valText)
        {
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, 6, 0, 0);
            Ellipse d = new Ellipse();
            d.Width = 8; d.Height = 8;
            d.Fill = dot;
            d.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(d);
            TextBlock lb = new TextBlock();
            lb.Text = label;
            lb.FontSize = 12;
            lb.Foreground = FgSec;
            lb.Margin = new Thickness(8, 0, 0, 0);
            lb.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(lb);
            TextBlock v = new TextBlock();
            v.FontSize = 11;
            v.Margin = new Thickness(10, 0, 0, 0);
            v.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(v);
            valText = v;
            return row;
        }

        private void BuildUi()
        {
            Border outer = new Border();
            outer.Margin = new Thickness(10);
            outer.CornerRadius = new CornerRadius(14);
            outer.Background = BgWin;
            outer.BorderBrush = BorderC;
            outer.BorderThickness = new Thickness(1);
            outer.Effect = new DropShadowEffect() { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 6, Opacity = 0.55 };
            Content = outer;

            Grid root = new Grid();
            outer.Child = root;
            root.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(44) });
            root.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition() { Height = GridLength.Auto });

            // 标题栏
            Grid titleBar = new Grid();
            titleBar.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e) { try { DragMove(); } catch { } };
            root.Children.Add(titleBar);
            StackPanel titleL = new StackPanel();
            titleL.Orientation = Orientation.Horizontal;
            titleL.Margin = new Thickness(16, 0, 0, 0);
            titleL.VerticalAlignment = VerticalAlignment.Center;
            Ellipse dot = new Ellipse();
            dot.Width = 10; dot.Height = 10;
            dot.Fill = Accent;
            dot.VerticalAlignment = VerticalAlignment.Center;
            titleL.Children.Add(dot);
            TextBlock t1 = new TextBlock();
            t1.Text = "DeepSeek Harness 一键安装";
            t1.FontSize = 13; t1.FontWeight = FontWeights.SemiBold;
            t1.Foreground = FgPri;
            t1.Margin = new Thickness(8, 0, 0, 0);
            titleL.Children.Add(t1);
            titleBar.Children.Add(titleL);

            // 内容区（4 页，可滚动）
            ScrollViewer scroller = new ScrollViewer();
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Grid.SetRow(scroller, 1);
            root.Children.Add(scroller);
            ContentGrid = new Grid();
            scroller.Content = ContentGrid;
            Pages = new StackPanel[4];
            for (int i = 0; i < 4; i++)
            {
                StackPanel p = new StackPanel();
                p.Margin = new Thickness(24, 10, 24, 0);
                p.Visibility = Visibility.Collapsed;
                ContentGrid.Children.Add(p);
                Pages[i] = p;
            }
            BuildPage0();
            BuildPage1();
            BuildPage2();
            BuildPage3();

            // 底部按钮
            Grid foot = new Grid();
            foot.Margin = new Thickness(20, 6, 20, 18);
            Grid.SetRow(foot, 2);
            root.Children.Add(foot);
            foot.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            foot.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            foot.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            TxtFooter = new TextBlock();
            TxtFooter.FontSize = 10.5;
            TxtFooter.Foreground = FgMut;
            TxtFooter.VerticalAlignment = VerticalAlignment.Center;
            foot.Children.Add(TxtFooter);
            BtnPrev = MakeButton("上一步", BgAlt, 88, 32, delegate() { if (Step > 0) ShowPage(Step - 1); });
            Grid.SetColumn(BtnPrev, 1);
            BtnPrev.Margin = new Thickness(0, 0, 8, 0);
            foot.Children.Add(BtnPrev);
            BtnNext = MakeButton("下一步", Accent, 96, 32, delegate() { if (NextAction != null) NextAction(); });
            Grid.SetColumn(BtnNext, 2);
            BtnNext.Margin = new Thickness(0, 0, 8, 0);
            foot.Children.Add(BtnNext);
            BtnCancel = MakeButton("关闭", BgAlt, 88, 32, delegate() { Close(); });
            Grid.SetColumn(BtnCancel, 3);
            foot.Children.Add(BtnCancel);
        }

        private Border Card()
        {
            Border card = new Border();
            card.Margin = new Thickness(0, 14, 0, 0);
            card.CornerRadius = new CornerRadius(10);
            card.Background = BgCard;
            card.Padding = new Thickness(16, 12, 16, 12);
            card.Child = new StackPanel();
            return card;
        }

        private void BuildPage0()
        {
            StackPanel p = Pages[0];
            TextBlock t = new TextBlock();
            t.Text = "欢迎";
            t.FontSize = 18; t.FontWeight = FontWeights.SemiBold;
            t.Foreground = FgPri;
            p.Children.Add(t);
            TextBlock d = new TextBlock();
            d.Text = "将安装 DeepSeek Harness 服务管理器及其运行环境（Node.js + dsh），全程离线，无需管理员权限。";
            d.FontSize = 12;
            d.Foreground = FgSec;
            d.TextWrapping = TextWrapping.Wrap;
            d.Margin = new Thickness(0, 8, 0, 0);
            d.LineHeight = 22;
            p.Children.Add(d);

            Border card = Card();
            p.Children.Add(card);
            StackPanel cp = (StackPanel)card.Child;
            TextBlock c1 = new TextBlock();
            c1.Text = "环境检测";
            c1.FontSize = 12; c1.FontWeight = FontWeights.SemiBold;
            c1.Foreground = FgPri;
            cp.Children.Add(c1);
            cp.Children.Add(MakeCheckItem(Green, ".NET Framework 4.8（必需）", out TxtDetectNet));
            cp.Children.Add(MakeCheckItem(Green, "Google Chrome（应用模式，可选）", out TxtDetectChrome));
            cp.Children.Add(MakeCheckItem(Amber, "已安装的 Node.js / dsh（检测信息）", out TxtDetectNode));
        }

        private void BuildPage1()
        {
            StackPanel p = Pages[1];
            TextBlock t = new TextBlock();
            t.Text = "安装位置与选项";
            t.FontSize = 18; t.FontWeight = FontWeights.SemiBold;
            t.Foreground = FgPri;
            p.Children.Add(t);

            Border card = Card();
            p.Children.Add(card);
            StackPanel cp = (StackPanel)card.Child;
            TextBlock c1 = new TextBlock();
            c1.Text = "安装目录";
            c1.FontSize = 12; c1.FontWeight = FontWeights.SemiBold;
            c1.Foreground = FgPri;
            cp.Children.Add(c1);
            Grid dirRow = new Grid();
            dirRow.Margin = new Thickness(0, 10, 0, 0);
            dirRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(1, GridUnitType.Star) });
            dirRow.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
            cp.Children.Add(dirRow);
            TxtInstallDir = new TextBox();
            TxtInstallDir.Text = InstallDir;
            TxtInstallDir.Height = 30;
            TxtInstallDir.FontSize = 11;
            TxtInstallDir.VerticalContentAlignment = VerticalAlignment.Center;
            TxtInstallDir.Padding = new Thickness(8, 0, 8, 0);
            TxtInstallDir.Background = BgLog;
            TxtInstallDir.Foreground = FgPri;
            TxtInstallDir.CaretBrush = FgPri;
            TxtInstallDir.BorderBrush = BorderC;
            TxtInstallDir.BorderThickness = new Thickness(1);
            dirRow.Children.Add(TxtInstallDir);
            BtnBrowse = MakeButton("浏览…", BgAlt, 64, 30, delegate()
            {
                var fbd = new System.Windows.Forms.FolderBrowserDialog();
                fbd.Description = "选择安装目录";
                fbd.SelectedPath = TxtInstallDir.Text;
                if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    TxtInstallDir.Text = fbd.SelectedPath;
            });
            Grid.SetColumn(BtnBrowse, 1);
            BtnBrowse.Margin = new Thickness(8, 0, 0, 0);
            dirRow.Children.Add(BtnBrowse);

            Border optCard = Card();
            p.Children.Add(optCard);
            StackPanel ocp = (StackPanel)optCard.Child;
            TextBlock c2 = new TextBlock();
            c2.Text = "选项";
            c2.FontSize = 12; c2.FontWeight = FontWeights.SemiBold;
            c2.Foreground = FgPri;
            ocp.Children.Add(c2);
            ChkShortcut = MakeCheckRow("创建桌面快捷方式", ChkShortcutVal, out LblShortcut);
            ocp.Children.Add(ChkShortcut);
            ChkAutoStart = MakeCheckRow("开机自动启动", ChkAutoStartVal, out LblAutoStart);
            ocp.Children.Add(ChkAutoStart);

            TextBlock note = new TextBlock();
            note.Text = "提示：安装到新位置将保留旧位置的设置（端口/主题等）。";
            note.FontSize = 10.5;
            note.Foreground = FgMut;
            note.Margin = new Thickness(0, 12, 0, 0);
            note.TextWrapping = TextWrapping.Wrap;
            p.Children.Add(note);
        }

        private StackPanel MakeCheckRow(string label, bool[] val, out TextBlock valLabel)
        {
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, 10, 0, 0);
            row.Cursor = Cursors.Hand;
            Border box = new Border();
            box.Width = 16; box.Height = 16;
            box.CornerRadius = new CornerRadius(4);
            box.BorderBrush = BorderC;
            box.BorderThickness = new Thickness(1);
            box.Background = val[0] ? Accent : Brushes.Transparent;
            box.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(box);
            TextBlock lb = new TextBlock();
            lb.Text = label;
            lb.FontSize = 12;
            lb.Foreground = FgSec;
            lb.Margin = new Thickness(8, 0, 0, 0);
            lb.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(lb);
            valLabel = lb;
            row.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                val[0] = !val[0];
                box.Background = val[0] ? Accent : Brushes.Transparent;
            };
            return row;
        }

        private void BuildPage2()
        {
            StackPanel p = Pages[2];
            TextBlock t = new TextBlock();
            t.Text = "正在安装…";
            t.FontSize = 18; t.FontWeight = FontWeights.SemiBold;
            t.Foreground = FgPri;
            p.Children.Add(t);
            ProgBar = new ProgressBar();
            ProgBar.Height = 8;
            ProgBar.Margin = new Thickness(0, 20, 0, 0);
            ProgBar.Foreground = Accent;
            ProgBar.Background = BgAlt;
            ProgBar.Minimum = 0; ProgBar.Maximum = 100;
            p.Children.Add(ProgBar);
            TxtProgress = new TextBlock();
            TxtProgress.FontSize = 11.5;
            TxtProgress.Foreground = FgSec;
            TxtProgress.Margin = new Thickness(0, 10, 0, 0);
            p.Children.Add(TxtProgress);
        }

        private void BuildPage3()
        {
            StackPanel p = Pages[3];
            TextBlock t = new TextBlock();
            t.Text = "安装完成";
            t.FontSize = 18; t.FontWeight = FontWeights.SemiBold;
            t.Foreground = FgPri;
            p.Children.Add(t);
            TextBlock d = new TextBlock();
            d.Text = "DeepSeek Harness 服务管理器已安装到：";
            d.FontSize = 12;
            d.Foreground = FgSec;
            d.Margin = new Thickness(0, 10, 0, 0);
            p.Children.Add(d);
            TxtDoneDir = new TextBlock();
            TxtDoneDir.Text = InstallDir;
            TxtDoneDir.FontSize = 11.5;
            TxtDoneDir.Foreground = FgPri;
            TxtDoneDir.FontFamily = new FontFamily("Consolas");
            TxtDoneDir.TextWrapping = TextWrapping.Wrap;
            TxtDoneDir.Margin = new Thickness(0, 4, 0, 0);
            p.Children.Add(TxtDoneDir);
            StackPanel row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, 14, 0, 0);
            row.Cursor = Cursors.Hand;
            Border box = new Border();
            box.Width = 16; box.Height = 16;
            box.CornerRadius = new CornerRadius(4);
            box.BorderBrush = BorderC;
            box.BorderThickness = new Thickness(1);
            box.Background = Accent;
            box.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(box);
            TextBlock lb = new TextBlock();
            lb.Text = "立即启动 DeepSeek Harness";
            lb.FontSize = 12;
            lb.Foreground = FgSec;
            lb.Margin = new Thickness(8, 0, 0, 0);
            lb.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(lb);
            LaunchNow = true;
            row.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                LaunchNow = !LaunchNow;
                box.Background = LaunchNow ? Accent : Brushes.Transparent;
            };
            p.Children.Add(row);
        }

        // ---------------- 页面切换 ----------------
        private Action NextAction;

        private void ShowPage(int idx)
        {
            Step = idx;
            for (int i = 0; i < Pages.Length; i++)
                Pages[i].Visibility = (i == idx) ? Visibility.Visible : Visibility.Collapsed;
            BtnPrev.Visibility = (idx == 0) ? Visibility.Collapsed : Visibility.Visible;
            BtnCancel.Visibility = (idx == 2 || idx == 3) ? Visibility.Collapsed : Visibility.Visible;
            if (idx == 3)
            {
                BtnNext.Visibility = Visibility.Visible;
                SetButtonText(BtnNext, "完成");
                NextAction = delegate()
                {
                    if (LaunchNow)
                    {
                        try { Process.Start(Path.Combine(InstallDir, "DeepSeekHarnessLauncher.exe")); } catch { }
                    }
                    Close();
                };
            }
            else
            {
                BtnNext.Visibility = (idx == 2) ? Visibility.Collapsed : Visibility.Visible;
                SetButtonText(BtnNext, "下一步");
                NextAction = delegate() { OnNext(); };
            }
            TxtFooter.Text = "";
        }

        private void SetButtonText(Border b, string text)
        {
            TextBlock t = b.Child as TextBlock;
            if (t != null) t.Text = text;
        }

        private void OnNext()
        {
            if (Step == 0) { ShowPage(1); }
            else if (Step == 1)
            {
                InstallDir = TxtInstallDir.Text.Trim();
                if (String.IsNullOrEmpty(InstallDir)) { TxtFooter.Text = "请选择安装目录"; return; }
                ShowPage(2);
                StartInstall();
            }
        }

        // ---------------- 环境检测 ----------------
        private void RunChecks()
        {
            // .NET 4.8
            bool net48 = false;
            try
            {
                RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
                if (k != null) { int rel = Convert.ToInt32(k.GetValue("Release", 0)); k.Close(); net48 = rel >= 528040; }
            }
            catch { }
            TxtDetectNet.Text = net48 ? "已就绪" : "未安装（需 .NET 4.8）";
            TxtDetectNet.Foreground = net48 ? Green : Red;

            // Chrome
            bool chrome = File.Exists(@"C:\Program Files\Google\Chrome\Application\chrome.exe") ||
                          File.Exists(@"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe");
            TxtDetectChrome.Text = chrome ? "已安装（应用模式可用）" : "未检测到（将使用默认浏览器）";
            TxtDetectChrome.Foreground = chrome ? Green : Amber;

            // node
            bool node = false;
            try
            {
                Process p = new Process();
                p.StartInfo.FileName = "where";
                p.StartInfo.Arguments = "node";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.Start();
                string o = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                node = o.Trim().Length > 0;
            }
            catch { }
            TxtDetectNode.Text = node ? "已检测到系统 Node.js（本次仍安装捆绑版本以保证一致）" : "未检测到（将安装捆绑版本）";
            TxtDetectNode.Foreground = Amber;
        }

        // ---------------- 安装 ----------------
        private void StartInstall()
        {
            SetupLogPath = Path.Combine(InstallDir, "setup.log");
            BtnPrev.Visibility = Visibility.Collapsed;
            Task.Factory.StartNew(delegate()
            {
                try
                {
                    Install();
                    Dispatcher.BeginInvoke(new Action(delegate()
                    {
                        Installed = true;
                        if (TxtDoneDir != null) TxtDoneDir.Text = InstallDir;
                        ShowPage(3);
                    }));
                }
                catch (Exception ex)
                {
                    Log("INSTALL FAILED: " + ex.Message);
                    Dispatcher.BeginInvoke(new Action(delegate()
                    {
                        TxtProgress.Text = "安装失败：" + ex.Message;
                        Rollback();
                        BtnNext.Visibility = Visibility.Visible;
                        BtnPrev.Visibility = Visibility.Collapsed;
                        BtnCancel.Visibility = Visibility.Visible;
                    }));
                }
            });
        }

        private void Install()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            SetProgress(2, "准备安装目录…");
            Log("Install dir: " + InstallDir);
            Directory.CreateDirectory(InstallDir);
            Directory.CreateDirectory(Path.Combine(InstallDir, "node"));
            Directory.CreateDirectory(Path.Combine(InstallDir, "dsh"));
            Directory.CreateDirectory(Path.Combine(InstallDir, "logs"));

            SetProgress(5, "解压启动器…");
            ExtractResource(asm, "launcher.exe", Path.Combine(InstallDir, "DeepSeekHarnessLauncher.exe"));
            ExtractResource(asm, "icon.ico", Path.Combine(InstallDir, "DeepSeek Harness.ico"));
            Log("launcher.exe copied");

            SetProgress(15, "解压 Node.js 运行时…");
            ExtractResource(asm, "node.zip", Path.Combine(Path.GetTempPath(), "dsh-node.zip"));
            string nodeSrc = FindNodeExe(Path.Combine(Path.GetTempPath(), "dsh-node.zip"));
            if (nodeSrc == null) throw new Exception("Node.js 包损坏");
            File.Copy(nodeSrc, Path.Combine(InstallDir, "node", "node.exe"), true);
            File.Delete(Path.Combine(Path.GetTempPath(), "dsh-node.zip"));
            Log("node.exe copied");

            SetProgress(70, "解压 dsh 包…");
            ExtractResource(asm, "dsh.zip", Path.Combine(Path.GetTempPath(), "dsh-tmp.zip"));
            using (ZipArchive za = ZipFile.OpenRead(Path.Combine(Path.GetTempPath(), "dsh-tmp.zip")))
            {
                za.ExtractToDirectory(Path.Combine(InstallDir, "dsh"));
            }
            File.Delete(Path.Combine(Path.GetTempPath(), "dsh-tmp.zip"));
            Log("dsh extracted");

            SetProgress(88, "创建快捷方式与注册…");
            if (ChkShortcutVal[0]) CreateShortcut(InstallDir);
            if (ChkAutoStartVal[0]) SetAutoStart(InstallDir);
            RegisterUninstall(InstallDir);
            Log("shortcut/autostart/uninstall-reg done");

            SetProgress(100, "完成");
        }

        private string FindNodeExe(string zipPath)
        {
            try
            {
                using (ZipArchive za = ZipFile.OpenRead(zipPath))
                {
                    foreach (ZipArchiveEntry e in za.Entries)
                    {
                        if (e.FullName.EndsWith("node.exe", StringComparison.OrdinalIgnoreCase) &&
                            !e.FullName.Contains("npm") && !e.FullName.Contains("node_modules"))
                        {
                            string tmp = Path.Combine(Path.GetTempPath(), "dsh-node.exe");
                            e.ExtractToFile(tmp, true);
                            return tmp;
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private void ExtractResource(Assembly asm, string name, string destPath)
        {
            using (Stream s = asm.GetManifestResourceStream(name))
            {
                if (s == null) throw new Exception("资源缺失: " + name);
                using (FileStream fs = File.Create(destPath))
                    s.CopyTo(fs);
            }
        }

        private void SetProgress(int pct, string msg)
        {
            Dispatcher.BeginInvoke(new Action(delegate()
            {
                ProgBar.Value = pct;
                TxtProgress.Text = msg;
            }));
        }

        private void CreateShortcut(string dir)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(t);
                dynamic sc = shell.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DeepSeek Harness.lnk"));
                sc.TargetPath = Path.Combine(dir, "DeepSeekHarnessLauncher.exe");
                sc.IconLocation = Path.Combine(dir, "DeepSeek Harness.ico") + ",0";
                sc.WorkingDirectory = dir;
                sc.Description = "DeepSeek Harness 服务管理器";
                sc.Save();
            }
            catch { }
        }

        private void SetAutoStart(string dir)
        {
            try
            {
                RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (k != null) { k.SetValue("DSHLauncher", "\"" + Path.Combine(dir, "DeepSeekHarnessLauncher.exe") + "\""); k.Close(); }
            }
            catch { }
        }

        private void RegisterUninstall(string dir)
        {
            try
            {
                RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DeepSeekHarnessLauncher");
                k.SetValue("DisplayName", "DeepSeek Harness 服务管理器");
                k.SetValue("DisplayVersion", "1.0.0");
                k.SetValue("Publisher", "Lirenluohua");
                k.SetValue("InstallLocation", dir);
                k.SetValue("DisplayIcon", Path.Combine(dir, "DeepSeek Harness.ico"));
                k.SetValue("UninstallString", "\"" + Path.Combine(dir, "DeepSeekHarnessLauncher.exe") + "\" --uninstall");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", 60000, RegistryValueKind.DWord);
                k.Close();
            }
            catch { }
        }

        private void Rollback()
        {
            try
            {
                if (Directory.Exists(InstallDir))
                {
                    // 保留 setup.log 用于排查
                    Directory.Delete(InstallDir, true);
                }
                TxtFooter.Text = "已回滚清理";
            }
            catch { }
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
            try
            {
                Run();
            }
            catch (Exception ex)
            {
                try
                {
                    string logPath = Path.Combine(Path.GetTempPath(), "dsh-setup-error.log");
                    File.WriteAllText(logPath, ex.ToString(), Encoding.UTF8);
                }
                catch { }
                throw;
            }
        }

        private static void Run()
        {
            try { SetProcessDpiAwarenessContext((IntPtr)(-4)); } catch { }
            Application app = new Application();
            SetupWindow win = new SetupWindow();
            app.Run(win);
        }
    }
}
