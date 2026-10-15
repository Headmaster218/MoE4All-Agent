using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class MoE4All
{
    private const int KillOnJobClose = 0x2000;
    private const int ExtendedLimitInformation = 9;
    private const int WebPort = 17373;
    private const string ProgressPrefix = "@@MOE4ALL_PROGRESS\t";
    private const string UpdatePrefix = "@@MOE4ALL_UPDATE\t";
    private static readonly object LogLock = new object();
    private static StreamWriter log;
    private static string logPath;
    private static AgentWindow window;

    private static class Presentation
    {
        public static string Language = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh" : "en";
        public static string Theme = "gray";
        public static string Preference = "gray";
        public static string DarkPreference = "gray";
        private static readonly Dictionary<string, string[]> Copy = new Dictionary<string, string[]>();

        static Presentation()
        {
            Add("启动中", "Starting"); Add("已就绪", "Ready"); Add("需要处理", "Needs attention");
            Add("在浏览器中打开", "Open in browser"); Add("检查更新", "Check updates");
            Add("发现更新", "Update available"); Add("更新中...", "Updating..."); Add("检查中...", "Checking...");
            Add("首次启动", "FIRST LAUNCH"); Add("MOE4ALL AGENT  /  首次启动", "MOE4ALL AGENT  /  FIRST LAUNCH"); Add("专家正在登机", "Experts are boarding");
            Add("正在唤醒你的 Agent", "Waking up your Agent");
            Add("这是第一次启动。MoE4All 正在准备 Agent 运行时、内置插件和独立工作区。只需一次，完成后会自动进入。", "Welcome to MoE4All. We are preparing the Agent runtime, built-in plugins and your workspace. This happens once; the Agent opens when everything is ready.");
            Add("正在连接本地运行时、内置插件和工作区，很快就好。", "Connecting your local runtime, plugins and workspace. Almost there.");
            Add("正在核验登机清单", "Checking the boarding list"); Add("正在检查本地运行时", "Checking the local runtime");
            Add("准备中", "Preparing"); Add("运行时、插件与工作区将在本机完成准备。", "Preparing the runtime, plugins and workspace on this computer.");
            Add("正在复用已经准备好的本地资源。", "Reusing your prepared local resources.");
            Add("启动详情", "Startup details"); Add("查看启动详情", "Show startup details"); Add("收起启动详情", "Hide startup details");
            Add("专家抵达后，后续启动会直接复用这套本地环境。", "Once the experts arrive, future launches reuse this local environment.");
            Add("本地环境已就位，正在等待 Agent 响应。", "The local environment is ready. Waiting for the Agent.");
            Add("安装 WebView2 运行时", "Install WebView2 Runtime"); Add("打开启动日志", "Open startup log");
            Add("正在连接插件与工作区", "Connecting plugins and workspace"); Add("Agent 即将醒来", "Your Agent is almost awake");
            Add("运行时正在装舱", "Loading the runtime onboard"); Add("专家正在飞往本地工作区", "Experts are flying to your workspace");
            Add("即将抵达，正在整理最后一排座位", "Almost there. Making room for the last few experts"); Add("专家即将落地", "Experts are about to land");
            Add("正在准备本地运行时。", "Preparing the local runtime."); Add("正在核验整合包中的运行时文件。", "Verifying the bundled runtime files.");
            Add("正在解压运行时、内置插件与工作区模板。", "Unpacking the runtime, plugins and workspace template.");
            Add("本地运行时已经准备完成。", "The local runtime is ready.");
            Add("专家已落地，正在唤醒 Agent", "The experts have landed. Waking up your Agent");
            Add("本地环境就绪，正在唤醒 Agent", "Local environment ready. Waking up your Agent");
            Add("正在启动桌面界面与本地服务。", "Starting the desktop interface and local services.");
            Add("启动遇到问题", "Startup needs attention"); Add("MoE4All 没有继续运行。具体原因已展开在下方，修正后重新启动即可。", "MoE4All could not continue. The details below explain what happened. Resolve the issue and restart.");
            Add("需要处理后再继续", "Resolve the issue to continue"); Add("已停止", "Stopped");
            Add("MoE4All Agent 更新", "MoE4All Agent updates"); Add("Agent 更新", "Agent updates"); Add("已安装  ", "Installed  ");
            Add("可以检查更新", "Ready to check"); Add("检查 GitHub 上是否有更新的 MoE4All Agent。", "Check GitHub for a newer MoE4All Agent package.");
            Add("查看发布页面", "View release page"); Add("检查更新", "Check for updates"); Add("关闭", "Close");
            Add("正在检查更新", "Checking for updates"); Add("正在连接 MoE4All 官方发布源。", "Contacting the official MoE4All release feed.");
            Add("正在检查版本信息", "Checking release information"); Add("已是最新版本", "You're up to date");
            Add(" 是当前最新的 Agent 版本。", " is the newest available Agent version."); Add("无需更新", "No update is required");
            Add("再次检查", "Check again"); Add(" 可更新", " is available");
            Add("仅替换应用文件。模型、对话和缓存留在原处；保留上一个版本用于回退。", "Download and replace application files only. Models, conversations and caches stay in place; one previous version is kept for rollback.");
            Add("整包大小  ", "Package size  "); Add("可以开始下载", "Ready to download"); Add("下载并安装", "Download and install"); Add("暂不更新", "Not now");
            Add("正在下载更新", "Downloading update"); Add("下载期间可以继续使用 MoE4All Agent。", "MoE4All Agent remains usable while the package downloads."); Add("请稍候", "Please wait");
            Add("正在校验下载", "Verifying download"); Add("修改应用文件前，先校验安装包的 SHA-256。", "Checking the package SHA-256 before any application files are changed.");
            Add("正在准备更新", "Preparing update"); Add("正在当前版本旁解压并验证新版本。", "Unpacking and validating the new application beside the current version.");
            Add("正在安装并重启", "Installing and restarting"); Add("Agent 窗口会短暂关闭，切换版本后自动重新打开。", "The Agent window will close briefly, switch versions, and reopen automatically.");
            Add("开始更新", "Starting update"); Add("正在准备应用更新。", "Preparing a secure application-only update.");
            Add("更新需要处理", "Update needs attention"); Add("未替换任何应用文件", "No application files were replaced"); Add("重试", "Try again");
            Add("选择 MoE4All 工作区目录", "Select a MoE4All workspace directory");
            Add("选择 MoE4All GGUF 模型", "Select a MoE4All GGUF model");
            Add("选择 MoE4All 引擎安装包或 infr.exe", "Select a MoE4All engine package or infr.exe");
            Add("GGUF 模型 (*.gguf)|*.gguf|所有文件 (*.*)|*.*", "GGUF model (*.gguf)|*.gguf|All files (*.*)|*.*");
            Add("森林绿", "Forest"); Add("黑色", "Black"); Add("灰色", "Gray"); Add("白色", "White");
            Add("确定", "OK");
            Add("此 Windows 会话中已有 MoE4All Agent 正在运行。", "MoE4All Agent is already running in this Windows session.");
            Add("此开发预览未包含整包更新器，请使用完整的 MoE4All Agent 安装包。", "The bundled updater is unavailable. Reinstall the complete MoE4All Agent package.");
        }

        private static void Add(string zh, string en)
        {
            string[] pair = { zh, en };
            Copy[zh] = pair; Copy[en] = pair;
        }

        public static string Translate(string source)
        {
            string[] pair;
            if (Copy.TryGetValue(source, out pair)) return pair[Language == "zh" ? 0 : 1];
            foreach (KeyValuePair<string, string[]> entry in Copy)
            {
                if (source.StartsWith(entry.Key, StringComparison.Ordinal))
                    return entry.Value[Language == "zh" ? 0 : 1] + source.Substring(entry.Key.Length);
                if (source.EndsWith(entry.Key, StringComparison.Ordinal))
                    return source.Substring(0, source.Length - entry.Key.Length) + entry.Value[Language == "zh" ? 0 : 1];
            }
            return source;
        }

        public static void SetCopy(Control control, string source)
        {
            control.Tag = source;
            control.Text = Translate(source);
        }

        public static Color ColorFor(string role)
        {
            bool light = Theme == "white";
            if (role == "error") return ColorTranslator.FromHtml(light ? "#ad343b" : "#f5a3aa");
            if (role == "success") return ColorTranslator.FromHtml(light ? "#2d6e4b" : "#a6d6b7");
            string[] palette = Theme == "green" ? new string[] { "#1e201e", "#252a26", "#e5ece5", "#bdc9bf", "#afbeb2", "#829487", "#b8d2bf", "#243b2b" }
                : Theme == "black" ? new string[] { "#101112", "#18191b", "#f1f2f3", "#bec1c5", "#a6abb2", "#69737f", "#e5e7eb", "#15171a" }
                : light ? new string[] { "#fafbfc", "#ffffff", "#24282d", "#58616b", "#59636e", "#7a8692", "#344b5e", "#ffffff" }
                : new string[] { "#1e1e1f", "#242426", "#ededee", "#c1c1c6", "#b2b2b7", "#818187", "#dedee2", "#252528" };
            int index = role == "base" ? 0 : role == "surface" ? 1 : role == "text" ? 2 : role == "muted" ? 3 : role == "faint" ? 4 : role == "border" ? 5 : role == "accent" ? 6 : 7;
            return ColorTranslator.FromHtml(palette[index]);
        }

        public static void Refresh(Control control)
        {
            string source = control.Tag as string;
            if (source != null) control.Text = Translate(source);
            control.BackColor = ColorFor(control is Button || control is ComboBox || control is TextBox ? "surface" : "base");
            control.ForeColor = ColorFor(control is Label && !control.Font.Bold ? "muted" : "text");
            Button button = control as Button;
            if (button != null)
            {
                button.FlatAppearance.BorderColor = ColorFor("border");
                if (button.Name == "primary") { button.BackColor = ColorFor("accent"); button.ForeColor = ColorFor("onAccent"); }
            }
            LinkLabel link = control as LinkLabel;
            if (link != null) { link.LinkColor = ColorFor("accent"); link.ActiveLinkColor = link.LinkColor; link.VisitedLinkColor = link.LinkColor; }
            foreach (Control child in control.Controls) Refresh(child);
            control.Invalidate();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public int LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public int ActiveProcessLimit;
        public UIntPtr Affinity;
        public int PriorityClass, SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimits limits, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }

    private static ProcessStartInfo PowerShellStart(string root)
    {
        string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        ProcessStartInfo start = new ProcessStartInfo(powershell);
        start.WorkingDirectory = root;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.EnvironmentVariables.Remove("PSModulePath");
        return start;
    }

    private static Assembly ResolveBundledManagedAssembly(object sender, ResolveEventArgs arguments)
    {
        string name = new AssemblyName(arguments.Name).Name;
        if (name != "Microsoft.Web.WebView2.Core" && name != "Microsoft.Web.WebView2.WinForms") return null;
        string filename = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + ".dll");
        if (!File.Exists(filename)) return null;
        return Assembly.Load(File.ReadAllBytes(filename));
    }

    private static void RepairLauncherConfiguration(string root)
    {
        string active = Path.Combine(root, "MoE4All.exe.config");
        if (File.Exists(active)) return;
        string bundled = Path.Combine(root, "harness", "launcher", "MoE4All.exe.config");
        if (File.Exists(bundled)) File.Copy(bundled, active, false);
    }

    private static void WriteMessage(string message, bool error)
    {
        lock (LogLock)
        {
            if (error) Console.Error.WriteLine(message);
            else Console.WriteLine(message);
            if (log != null) log.WriteLine(message);
        }
        AgentWindow active = window;
        if (active != null) active.AppendLog(message, error);
    }

    private static void WriteMessage(string message)
    {
        WriteMessage(message, false);
    }

    private static bool TryHandleProgress(string message)
    {
        if (!message.StartsWith(ProgressPrefix, StringComparison.Ordinal)) return false;
        string[] parts = message.Split(new char[] { '\t' }, 4);
        long completed;
        long total;
        if (parts.Length != 4 || !long.TryParse(parts[1], out completed) || !long.TryParse(parts[2], out total)) return true;
        AgentWindow active = window;
        if (active != null) active.UpdatePreparationProgress(completed, total, parts[3]);
        return true;
    }

    private static int RunScript(IntPtr job, string root, string script, string arguments)
    {
        ProcessStartInfo start = PowerShellStart(root);
        start.Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Quote(script) + arguments;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardOutputEncoding = new UTF8Encoding(false);
        start.StandardErrorEncoding = new UTF8Encoding(false);
        using (Process child = new Process())
        {
            child.StartInfo = start;
            child.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
            {
                if (e.Data != null && !TryHandleProgress(e.Data)) WriteMessage(e.Data);
            };
            child.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) WriteMessage(e.Data, true); };
            if (!child.Start()) return 4;
            if (!AssignProcessToJobObject(job, child.Handle))
            {
                child.Kill();
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            child.BeginOutputReadLine();
            child.BeginErrorReadLine();
            child.WaitForExit();
            child.WaitForExit();
            return child.ExitCode;
        }
    }

    private static IntPtr CreateChildJob()
    {
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        ExtendedLimits limits = new ExtendedLimits();
        limits.BasicLimitInformation.LimitFlags = KillOnJobClose;
        if (!SetInformationJobObject(job, ExtendedLimitInformation, ref limits, (uint)Marshal.SizeOf(typeof(ExtendedLimits))))
        {
            CloseHandle(job);
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        return job;
    }

    private static void PrepareProductDirectories(string root)
    {
        string data = Path.Combine(root, "data");
        string legacy = Path.Combine(root, "harness", "agent-state");
        if (!Directory.Exists(data) && Directory.Exists(legacy))
        {
            Directory.Move(legacy, data);
            WriteMessage("Migrated legacy Agent state to " + data);
        }

        string models = Path.Combine(root, "models");
        string legacyModels = Path.Combine(data, "home", "moe4all-models");
        if (!Directory.Exists(models) && Directory.Exists(legacyModels))
        {
            Directory.Move(legacyModels, models);
            WriteMessage("Migrated the managed model library to " + models);
        }

        Directory.CreateDirectory(data);
        Directory.CreateDirectory(models);
        Directory.CreateDirectory(Path.Combine(root, "cache"));
        Directory.CreateDirectory(Path.Combine(root, "updates"));
        Directory.CreateDirectory(Path.Combine(data, "logs"));
    }

    private static void OpenUrl(string url)
    {
        try
        {
            ProcessStartInfo start = new ProcessStartInfo(url);
            start.UseShellExecute = true;
            Process.Start(start);
        }
        catch (Exception error)
        {
            WriteMessage("Could not open " + url + ": " + error.Message, true);
        }
    }

    private static void OpenLog(string root)
    {
        string path = Path.Combine(root, "data", "logs", "launcher.log");
        if (File.Exists(path)) OpenUrl(path);
    }

    [STAThread]
    private static int Main(string[] arguments)
    {
        AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
        AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
        AppDomain.CurrentDomain.AssemblyResolve += ResolveBundledManagedAssembly;
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
        }
        catch (IOException)
        {
            // The normal WinExe launcher intentionally has no console handle.
        }
        bool headless = Array.IndexOf(arguments, "--headless") >= 0 || Array.IndexOf(arguments, "--no-pause") >= 0;
        bool startupUiTest = Array.IndexOf(arguments, "--startup-ui-test") >= 0;
        string root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (startupUiTest) return RunStartupUiTest(root);
        try
        {
            RepairLauncherConfiguration(root);
            AgentWindow.LoadAppearance(root);
            PrepareProductDirectories(root);
            logPath = Path.Combine(root, "data", "logs", "launcher.log");
            log = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
            log.AutoFlush = true;
            WriteMessage("=== MoE4All Agent launch " + DateTime.UtcNow.ToString("o") + " ===");
            WriteMessage("Install: " + root);

            if (headless)
            {
                int result = RunHeadless(root);
                if (result != 0)
                {
                    WriteMessage("MoE4All Agent exited with code " + result + ".", true);
                    WriteMessage("Startup log: " + logPath, true);
                }
                return result;
            }

            bool first;
            using (Mutex mutex = new Mutex(true, "Local\\MoE4All-Agent", out first))
            {
                if (!first)
                {
                    AgentWindow.ShowNativeNotice(Presentation.Translate("MoE4All Agent is already running in this Windows session."));
                    return 2;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                window = new AgentWindow(root);
                Application.Run(window);
                window = null;
            }
            return 0;
        }
        catch (Exception error)
        {
            WriteMessage("MoE4All Agent could not start: " + error, true);
            if (!headless)
            {
                AgentWindow.ShowNativeNotice(Presentation.Translate("启动遇到问题") + "\r\n\r\n" + error.Message + "\r\n\r\nLog: " + logPath);
            }
            return 1;
        }
        finally
        {
            if (log != null) log.Dispose();
        }
    }

    private static int RunHeadless(string root)
    {
        bool first;
        using (Mutex mutex = new Mutex(true, "Local\\MoE4All-Agent-Headless", out first))
        {
            if (!first) return 2;
            string state = Path.Combine(root, "data");
            string profile = Path.Combine(state, "home", "profiles", "web", "node_modules");
            string run = Path.Combine(root, "harness", "scripts", "Run-Agent.ps1");
            string prepare = Path.Combine(root, "harness", "scripts", "Prepare-BundledRuntime.ps1");
            if (!File.Exists(run) || !File.Exists(prepare))
            {
                WriteMessage("The bundled Agent runtime is incomplete. Install the full MoE4All package, not MoE4All.exe alone.", true);
                return 3;
            }
            IntPtr job = CreateChildJob();
            try
            {
                int prepared = RunScript(job, root, prepare, " -InstallRoot " + Quote(root) + " -StateRoot " + Quote(state));
                if (prepared != 0 || !Directory.Exists(profile)) return prepared != 0 ? prepared : 3;
                return RunScript(job, root, run, " -StateRoot " + Quote(state));
            }
            finally
            {
                CloseHandle(job);
            }
        }
    }

    private static int RunStartupUiTest(string root)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (AgentWindow testWindow = new AgentWindow(root, false))
        {
            testWindow.ShowInTaskbar = false;
            testWindow.Opacity = 0;
            testWindow.Show();
            Application.DoEvents();
            Size[] sizes = { new Size(760, 520), new Size(1024, 640), new Size(1280, 760) };
            string screenshotDirectory = Path.Combine(root, "screenshots");
            Directory.CreateDirectory(screenshotDirectory);
            foreach (string language in new string[] { "zh", "en" })
            foreach (string theme in new string[] { "green", "black", "gray", "white" })
            foreach (Size size in sizes)
            {
                testWindow.SetAppearanceForTest(language, theme);
                foreach (string surface in new string[] { "base", "surface" })
                foreach (string foreground in new string[] { "text", "muted", "faint", "error", "success" })
                {
                    if (Contrast(Presentation.ColorFor(surface), Presentation.ColorFor(foreground)) < 4.5)
                        throw new InvalidOperationException("Native contrast failed: " + theme + " " + foreground + " on " + surface);
                }
                if (Contrast(Presentation.ColorFor("accent"), Presentation.ColorFor("onAccent")) < 4.5)
                    throw new InvalidOperationException("Native primary button contrast failed: " + theme);
                string error;
                if (!testWindow.ValidateStartupLayoutForTest(size, out error))
                {
                    Console.Error.WriteLine("Startup UI layout failed at " + size.Width + "x" + size.Height + ": " + error);
                    return 5;
                }
                if (size.Width == 1024)
                {
                    testWindow.ExportBootstrapForTest(screenshotDirectory, language + "-" + theme);
                    using (Bitmap bitmap = new Bitmap(testWindow.Width, testWindow.Height))
                    {
                        testWindow.DrawToBitmap(bitmap, new Rectangle(0, 0, testWindow.Width, testWindow.Height));
                        bitmap.Save(Path.Combine(screenshotDirectory, language + "-" + theme + ".png"));
                    }

                }
            }
        }
        Console.WriteLine("Startup UI passed: two languages, four palettes, three window sizes.");
        return 0;
    }

    private static double Luminance(Color color)
    {
        double result = 0;
        double[] components = { color.R / 255.0, color.G / 255.0, color.B / 255.0 };
        double[] weights = { 0.2126, 0.7152, 0.0722 };
        for (int index = 0; index < components.Length; index++)
            result += (components[index] <= 0.04045 ? components[index] / 12.92 : Math.Pow((components[index] + 0.055) / 1.055, 2.4)) * weights[index];
        return result;
    }

    private static double Contrast(Color a, Color b)
    {
        double first = Luminance(a), second = Luminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private sealed class StartupProgressIndicator : Control
    {
        private readonly System.Windows.Forms.Timer animation;
        private int progress;
        private int animationOffset;
        private bool indeterminate = true;
        private bool failed;

        public StartupProgressIndicator()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Height = 12;
            TabStop = false;
            animation = new System.Windows.Forms.Timer();
            animation.Interval = 28;
            animation.Tick += delegate
            {
                if (!indeterminate || Width <= 0) return;
                animationOffset = (animationOffset + Math.Max(4, Width / 100)) % Math.Max(1, Width + Math.Max(48, Width / 4));
                Invalidate();
            };
            animation.Start();
        }

        public int ProgressValue { get { return progress / 10; } }

        public void SetProgress(int value)
        {
            progress = Math.Max(0, Math.Min(1000, value));
            indeterminate = false;
            failed = false;
            Invalidate();
        }

        public void SetIndeterminate()
        {
            indeterminate = true;
            failed = false;
            animationOffset = 0;
            Invalidate();
        }

        public void SetFailed()
        {
            indeterminate = false;
            failed = true;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (ClientSize.Width < 2 || ClientSize.Height < 2) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int barHeight = Math.Min(8, Math.Max(4, ClientSize.Height - 2));
            Rectangle track = new Rectangle(0, (ClientSize.Height - barHeight) / 2, ClientSize.Width - 1, barHeight);
            using (GraphicsPath trackPath = RoundedRectangle(track, barHeight / 2))
            using (SolidBrush trackBrush = new SolidBrush(Presentation.ColorFor("border")))
            {
                e.Graphics.FillPath(trackBrush, trackPath);
                e.Graphics.SetClip(trackPath);
                Color fillColor = failed ? Presentation.ColorFor("error") : Presentation.ColorFor("accent");
                using (SolidBrush fillBrush = new SolidBrush(fillColor))
                {
                    if (indeterminate)
                    {
                        int segmentWidth = Math.Max(48, track.Width / 4);
                        Rectangle segment = new Rectangle(animationOffset - segmentWidth, track.Y, segmentWidth, track.Height);
                        using (GraphicsPath segmentPath = RoundedRectangle(segment, barHeight / 2))
                            e.Graphics.FillPath(fillBrush, segmentPath);
                    }
                    else if (progress > 0 || failed)
                    {
                        int fillWidth = failed ? track.Width : Math.Max(1, track.Width * progress / 1000);
                        Rectangle fill = new Rectangle(track.X, track.Y, fillWidth, track.Height);
                        using (GraphicsPath fillPath = RoundedRectangle(fill, barHeight / 2))
                            e.Graphics.FillPath(fillBrush, fillPath);
                    }
                }
                e.Graphics.ResetClip();
            }
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            if (diameter <= 1)
            {
                path.AddRectangle(bounds);
                return path;
            }
            Rectangle arc = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.X;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) animation.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class AppearanceButton : Button
    {
        private bool hovered;
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            bool primary = Name == "primary" && Enabled;
            Color fill = Presentation.ColorFor(primary ? (hovered ? "muted" : "accent") : FlatAppearance.BorderSize == 0 && !hovered ? "base" : "surface");
            Color text = Presentation.ColorFor(!Enabled ? "faint" : primary ? "onAccent" : "text");
            using (SolidBrush brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, ClientRectangle);
            if (FlatAppearance.BorderSize > 0)
                using (Pen pen = new Pen(Presentation.ColorFor("border")))
                    e.Graphics.DrawRectangle(pen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3), text, fill);
        }
    }

    private sealed class AgentWindow : Form
    {
        private readonly string root;
        private readonly bool firstLaunch;
        private readonly Label status;
        private readonly Label startupHeading;
        private readonly Label startupExplanation;
        private readonly Label progressText;
        private readonly Label progressDetail;
        private readonly Label progressPercent;
        private readonly StartupProgressIndicator progressBar;
        private readonly TextBox output;
        private readonly Panel startup;
        private readonly TableLayoutPanel startupLayout;
        private readonly Panel detailsPanel;
        private readonly FlowLayoutPanel startupActions;
        private readonly Button detailsButton;
        private readonly Button logButton;
        private readonly Button browserButton;
        private readonly Button runtimeButton;
        private readonly WebView2 webView;
        private readonly string installedVersion;
        private readonly ToolTip toolbarTip = new ToolTip();
        private Task browserInitialization;
        private bool bootstrapLoaded;
        private bool bootstrapFailed;
        private bool desktopBridgeReady;
        private bool desktopNotified;
        private string desktopPhase = "idle";
        private string desktopMessage = "";
        private long desktopCompleted, desktopTotal;
        private IntPtr job = IntPtr.Zero;
        private DesktopBridge bridge;
        private volatile bool closing;
        private volatile bool ready;
        private bool preparingProgress;
        private bool updateBusy;
        private bool updateInstalling;
        private bool updateStatusFailed;
        private bool revealUpdateResult;
        private string availableUpdate;
        private long availableUpdateBytes;

        public AgentWindow(string installRoot, bool startAgentOnShown = true)
        {
            root = installRoot;
            LoadAppearance(root);
            firstLaunch = !Directory.Exists(Path.Combine(root, "data", "home", "profiles", "web", "node_modules"));
            installedVersion = ReadInstalledVersion(root);
            Text = "MoE4All Agent";
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Rectangle workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            int initialWidth = Math.Max(680, Math.Min(1180, workingArea.Width - 48));
            int initialHeight = Math.Max(500, Math.Min(760, workingArea.Height - 48));
            MinimumSize = new Size(Math.Min(760, initialWidth), Math.Min(520, initialHeight));
            Size = new Size(initialWidth, initialHeight);
            BackColor = Presentation.ColorFor("base");
            ForeColor = Presentation.ColorFor("text");
            Font = new Font("Segoe UI", 9F);

            Panel toolbar = new Panel();
            toolbar.Dock = DockStyle.Top;
            toolbar.Height = 30;
            toolbar.BackColor = BackColor;
            status = new Label();
            status.AutoSize = false;
            status.Size = new Size(160, 24);
            status.Location = new Point(12, 3);
            Presentation.SetCopy(status, "Starting");
            toolbar.Controls.Add(status);
            browserButton = new AppearanceButton();
            browserButton.Text = "\uE774";
            browserButton.Font = new Font("Segoe MDL2 Assets", 12F);
            browserButton.AccessibleName = Presentation.Translate("Open in browser");
            browserButton.Dock = DockStyle.Right;
            browserButton.Width = 34;
            browserButton.FlatStyle = FlatStyle.Flat;
            browserButton.FlatAppearance.BorderSize = 0;
            browserButton.UseVisualStyleBackColor = false;
            browserButton.Click += delegate { OpenUrl("http://127.0.0.1:" + WebPort + "/"); };
            toolbar.Controls.Add(browserButton);
            webView = new WebView2();
            webView.Dock = DockStyle.Fill;
            webView.Visible = false;
            webView.BackColor = BackColor;

            startup = new Panel();
            startup.Dock = DockStyle.Fill;
            startup.BackColor = BackColor;
            startup.AutoScroll = true;

            startupLayout = new TableLayoutPanel();
            startupLayout.Dock = DockStyle.Fill;
            startupLayout.Padding = new Padding(52, 38, 52, 28);
            startupLayout.BackColor = BackColor;
            startupLayout.ColumnCount = 1;
            startupLayout.RowCount = 10;
            startupLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            startupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            startupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
            startupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            startupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12F));
            startupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            startupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 16F));
            startupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            startupLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            startupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22F));
            startupLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            startup.Controls.Add(startupLayout);

            Label eyebrow = new Label();
            Presentation.SetCopy(eyebrow, firstLaunch ? "MOE4ALL AGENT  /  FIRST LAUNCH" : "MOE4ALL AGENT");
            eyebrow.Dock = DockStyle.Fill;
            eyebrow.Font = new Font("Segoe UI Semibold", 9F);
            eyebrow.ForeColor = Presentation.ColorFor("accent");
            eyebrow.TextAlign = ContentAlignment.MiddleLeft;
            startupLayout.Controls.Add(eyebrow, 0, 0);

            startupHeading = new Label();
            Presentation.SetCopy(startupHeading, firstLaunch ? "专家正在登机" : "正在唤醒你的 Agent");
            startupHeading.Dock = DockStyle.Fill;
            startupHeading.Font = new Font("Microsoft YaHei UI", 24F, FontStyle.Bold);
            startupHeading.ForeColor = Presentation.ColorFor("text");
            startupHeading.TextAlign = ContentAlignment.MiddleLeft;
            startupLayout.Controls.Add(startupHeading, 0, 1);

            startupExplanation = new Label();
            Presentation.SetCopy(startupExplanation, firstLaunch
                ? "这是第一次启动。MoE4All 正在准备 Agent 运行时、内置插件和独立工作区。只需一次，完成后会自动进入。"
                : "正在连接本地运行时、内置插件和工作区，很快就好。");
            startupExplanation.Dock = DockStyle.Fill;
            startupExplanation.Font = new Font("Microsoft YaHei UI", 10F);
            startupExplanation.ForeColor = Presentation.ColorFor("muted");
            startupExplanation.TextAlign = ContentAlignment.TopLeft;
            startupLayout.Controls.Add(startupExplanation, 0, 2);

            TableLayoutPanel progressHeader = new TableLayoutPanel();
            progressHeader.Dock = DockStyle.Fill;
            progressHeader.ColumnCount = 2;
            progressHeader.RowCount = 1;
            progressHeader.Margin = Padding.Empty;
            progressHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            progressHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156F));
            progressHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            startupLayout.Controls.Add(progressHeader, 0, 4);

            progressText = new Label();
            Presentation.SetCopy(progressText, firstLaunch ? "正在核验登机清单" : "正在检查本地运行时");
            progressText.Dock = DockStyle.Fill;
            progressText.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            progressText.ForeColor = Presentation.ColorFor("text");
            progressText.TextAlign = ContentAlignment.MiddleLeft;
            progressHeader.Controls.Add(progressText, 0, 0);

            progressPercent = new Label();
            Presentation.SetCopy(progressPercent, "准备中");
            progressPercent.Dock = DockStyle.Fill;
            progressPercent.ForeColor = Presentation.ColorFor("faint");
            progressPercent.TextAlign = ContentAlignment.MiddleRight;
            progressHeader.Controls.Add(progressPercent, 1, 0);

            progressBar = new StartupProgressIndicator();
            progressBar.Dock = DockStyle.Fill;
            progressBar.Margin = new Padding(0, 2, 0, 2);
            startupLayout.Controls.Add(progressBar, 0, 5);

            progressDetail = new Label();
            Presentation.SetCopy(progressDetail, firstLaunch
                ? "运行时、插件与工作区将在本机完成准备。"
                : "正在复用已经准备好的本地资源。");
            progressDetail.Dock = DockStyle.Fill;
            progressDetail.Font = new Font("Microsoft YaHei UI", 9F);
            progressDetail.ForeColor = Presentation.ColorFor("faint");
            progressDetail.TextAlign = ContentAlignment.TopLeft;
            progressDetail.AutoEllipsis = true;
            startupLayout.Controls.Add(progressDetail, 0, 6);

            detailsPanel = new Panel();
            detailsPanel.Dock = DockStyle.Fill;
            detailsPanel.Margin = new Padding(0, 8, 0, 4);
            detailsPanel.Visible = false;
            startupLayout.Controls.Add(detailsPanel, 0, 7);

            TableLayoutPanel detailsLayout = new TableLayoutPanel();
            detailsLayout.Dock = DockStyle.Fill;
            detailsLayout.ColumnCount = 1;
            detailsLayout.RowCount = 2;
            detailsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            detailsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
            detailsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            detailsPanel.Controls.Add(detailsLayout);

            Label detailsHeading = new Label();
            Presentation.SetCopy(detailsHeading, "启动详情");
            detailsHeading.Dock = DockStyle.Fill;
            detailsHeading.ForeColor = Presentation.ColorFor("faint");
            detailsHeading.TextAlign = ContentAlignment.MiddleLeft;
            detailsLayout.Controls.Add(detailsHeading, 0, 0);

            output = new TextBox();
            output.Multiline = true;
            output.ReadOnly = true;
            output.ScrollBars = ScrollBars.Vertical;
            output.BorderStyle = BorderStyle.FixedSingle;
            output.BackColor = Presentation.ColorFor("surface");
            output.ForeColor = Presentation.ColorFor("muted");
            output.Font = new Font("Consolas", 9F);
            output.Dock = DockStyle.Fill;
            output.Margin = Padding.Empty;
            detailsLayout.Controls.Add(output, 0, 1);

            Label firstLaunchNote = new Label();
            Presentation.SetCopy(firstLaunchNote, firstLaunch
                ? "专家抵达后，后续启动会直接复用这套本地环境。"
                : "本地环境已就位，正在等待 Agent 响应。");
            firstLaunchNote.Dock = DockStyle.Fill;
            firstLaunchNote.Font = new Font("Microsoft YaHei UI", 8.5F);
            firstLaunchNote.ForeColor = Presentation.ColorFor("faint");
            firstLaunchNote.TextAlign = ContentAlignment.MiddleLeft;
            startupLayout.Controls.Add(firstLaunchNote, 0, 8);

            startupActions = new FlowLayoutPanel();
            startupActions.Dock = DockStyle.Fill;
            startupActions.Margin = Padding.Empty;
            startupActions.Padding = new Padding(0, 6, 0, 0);
            startupActions.FlowDirection = FlowDirection.LeftToRight;
            startupActions.WrapContents = false;
            startupLayout.Controls.Add(startupActions, 0, 9);

            detailsButton = CreateButton("查看启动详情");
            detailsButton.Width = 204;
            detailsButton.Margin = new Padding(0, 0, 10, 0);
            detailsButton.Click += delegate { SetDetailsVisible(!detailsPanel.Visible); };
            startupActions.Controls.Add(detailsButton);

            runtimeButton = CreateButton("Install WebView2 Runtime");
            runtimeButton.Visible = false;
            runtimeButton.Width = 198;
            runtimeButton.Margin = new Padding(0, 0, 10, 0);
            runtimeButton.Click += delegate { OpenUrl("https://developer.microsoft.com/microsoft-edge/webview2/"); };
            startupActions.Controls.Add(runtimeButton);

            logButton = CreateButton("打开启动日志");
            logButton.Width = 180;
            logButton.Margin = Padding.Empty;
            logButton.Click += delegate { OpenLog(root); };
            startupActions.Controls.Add(logButton);

            Controls.Add(webView);
            Controls.Add(startup);
            Controls.Add(toolbar);
            RefreshAppearance();
            Load += delegate { FitToWorkingArea(); ApplyWindowTheme(Handle); };
            SystemEvents.UserPreferenceChanged += OnSystemAppearanceChanged;
            if (startAgentOnShown) Shown += async delegate { await InitializeBrowser(); if (!closing) StartAgent(); };
            FormClosing += OnClosing;
        }

        public static void LoadAppearance(string root)
        {
            string path = Path.Combine(root, "data", "desktop-appearance.json");
            if (!File.Exists(path)) return;
            try
            {
                Dictionary<string, object> value = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                object language, theme, preference, dark;
                if (value.TryGetValue("language", out language) && (language as string == "en" || language as string == "zh")) Presentation.Language = (string)language;
                if (value.TryGetValue("darkPreference", out dark) && IsTheme(dark as string) && dark as string != "white") Presentation.DarkPreference = (string)dark;
                if (value.TryGetValue("preference", out preference) && (IsTheme(preference as string) || preference as string == "system")) Presentation.Preference = (string)preference;
                else if (value.TryGetValue("theme", out theme) && IsTheme(theme as string)) Presentation.Preference = (string)theme;
                ResolveSystemAppearance();
            }
            catch (Exception error) { WriteMessage("Could not load desktop appearance: " + error.Message); }
        }

        private static bool IsTheme(string theme)
        {
            return theme == "green" || theme == "black" || theme == "gray" || theme == "white";
        }

        private void SaveAppearance()
        {
            try
            {
                string directory = Path.Combine(root, "data");
                Directory.CreateDirectory(directory);
                string json = new JavaScriptSerializer().Serialize(new { language = Presentation.Language, theme = Presentation.Theme, preference = Presentation.Preference, darkPreference = Presentation.DarkPreference });
                File.WriteAllText(Path.Combine(directory, "desktop-appearance.json"), json, new UTF8Encoding(false));
            }
            catch (Exception error) { WriteMessage("Could not save desktop appearance: " + error.Message); }
        }

        private static void ResolveSystemAppearance()
        {
            bool dark = true;
            try
            {
                object setting = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0);
                dark = Convert.ToInt32(setting) == 0;
            }
            catch { }
            Presentation.Theme = Presentation.Preference == "system" ? (dark ? Presentation.DarkPreference : "white") : Presentation.Preference;
        }
        private void OnSystemAppearanceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (closing || !IsHandleCreated) return;
            try { BeginInvoke(new Action(delegate { ResolveSystemAppearance(); RefreshAppearance(); })); }
            catch (InvalidOperationException) { }
        }
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
        private static void ApplyWindowTheme(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            int dark = Presentation.Theme == "white" ? 0 : 1;
            int caption = ColorTranslator.ToWin32(Presentation.ColorFor("base"));
            int text = ColorTranslator.ToWin32(Presentation.ColorFor("text"));
            try
            {
                DwmSetWindowAttribute(handle, 20, ref dark, 4);
                DwmSetWindowAttribute(handle, 35, ref caption, 4);
                DwmSetWindowAttribute(handle, 36, ref text, 4);
            }
            catch (DllNotFoundException) { }
        }
        private void RefreshAppearance()
        {
            Presentation.Refresh(this);
            browserButton.BackColor = BackColor;
            toolbarTip.SetToolTip(browserButton, Presentation.Translate("Open in browser"));
            browserButton.AccessibleName = Presentation.Translate("Open in browser");
            ApplyWindowTheme(IsHandleCreated ? Handle : IntPtr.Zero);
            PushStartupState();
        }
        private void PublishDesktopState(bool open)
        {
            if (closing || IsDisposed || !ready || !desktopBridgeReady || webView.CoreWebView2 == null) return;
            if (open && desktopPhase == "available") desktopNotified = true;
            string json = new JavaScriptSerializer().Serialize(new {
                type = "moe4all-desktop-state", phase = desktopPhase,
                installedVersion = installedVersion, version = availableUpdate ?? "",
                completed = desktopCompleted, total = desktopTotal, message = desktopMessage, open = open
            });
            webView.CoreWebView2.PostWebMessageAsJson(json);
        }
        private void ReceiveAppearance(object sender, CoreWebView2WebMessageReceivedEventArgs message)
        {
            try
            {
                Dictionary<string, object> value = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(message.WebMessageAsJson);
                object type, command, language, theme, preference, dark;
                if (value == null || !value.TryGetValue("type", out type)) return;
                if (!ready && message.Source == "about:blank" && type as string == "moe4all-startup-command")
                {
                    if (!value.TryGetValue("command", out command)) return;
                    if (command as string == "log") OpenLog(root);
                    if (command as string == "runtime") OpenUrl("https://developer.microsoft.com/microsoft-edge/webview2/");
                    if (command as string == "browser") OpenUrl("http://127.0.0.1:" + WebPort + "/");
                    return;
                }
                Uri source;
                if (!ready || !Uri.TryCreate(message.Source, UriKind.Absolute, out source) || source.Scheme != "http" || source.Host != "127.0.0.1" || source.Port != WebPort) return;
                if (type as string == "moe4all-desktop-command")
                {
                    if (!value.TryGetValue("command", out command)) return;
                    if (command as string == "status")
                    {
                        desktopBridgeReady = true;
                        PublishDesktopState(desktopPhase == "available" && !desktopNotified);
                    }
                    if (command as string == "check") StartUpdateAction("Check", true);
                    if (command as string == "install" && !string.IsNullOrEmpty(availableUpdate)) StartUpdateAction("Install", true);
                    if (command as string == "browser") OpenUrl("http://127.0.0.1:" + WebPort + "/");
                    if (command as string == "release" && !string.IsNullOrEmpty(availableUpdate))
                        OpenUrl("https://github.com/Headmaster218/MoE4All-Agent/releases/tag/" + Uri.EscapeDataString(availableUpdate));
                    return;
                }
                if (type as string != "moe4all-appearance") return;
                if (!value.TryGetValue("language", out language) || (language as string != "en" && language as string != "zh")) return;
                if (!value.TryGetValue("theme", out theme) || !IsTheme(theme as string)) return;
                Presentation.Language = (string)language;
                Presentation.Theme = (string)theme;
                if (value.TryGetValue("preference", out preference) && (IsTheme(preference as string) || preference as string == "system")) Presentation.Preference = (string)preference;
                else Presentation.Preference = Presentation.Theme;
                if (value.TryGetValue("darkPreference", out dark) && IsTheme(dark as string) && dark as string != "white") Presentation.DarkPreference = (string)dark;
                SaveAppearance();
                RefreshAppearance();
            }
            catch (Exception error) { WriteMessage("Invalid desktop message: " + error.Message); }
        }

        private static string ReadInstalledVersion(string installRoot)
        {
            string manifest = Path.Combine(installRoot, "harness", "distribution", "agent-release.json");
            if (!File.Exists(manifest)) return "development build";
            try
            {
                Match match = Regex.Match(File.ReadAllText(manifest), "\\\"version\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"");
                return match.Success ? match.Groups[1].Value : "development build";
            }
            catch
            {
                return "development build";
            }
        }

        private static Button CreateButton(string text)
        {
            Button button = new AppearanceButton();
            Presentation.SetCopy(button, text);
            button.Height = 29;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Presentation.ColorFor("border");
            button.BackColor = Presentation.ColorFor("surface");
            button.ForeColor = Presentation.ColorFor("text");
            button.UseVisualStyleBackColor = false;
            return button;
        }

        public static void ShowNativeNotice(string message)
        {
            using (Form notice = new Form())
            {
                notice.Text = "MoE4All Agent";
                notice.StartPosition = FormStartPosition.CenterScreen;
                notice.AutoScaleMode = AutoScaleMode.Dpi;
                notice.Font = new Font("Segoe UI", 10F);
                notice.ClientSize = new Size(500, 250);
                notice.Padding = new Padding(24);
                notice.MinimumSize = new Size(420, 240);
                TextBox body = new TextBox();
                body.Multiline = true;
                body.ReadOnly = true;
                body.BorderStyle = BorderStyle.None;
                body.ScrollBars = ScrollBars.Vertical;
                body.Text = message;
                body.Dock = DockStyle.Fill;
                Button close = CreateButton("OK");
                close.Dock = DockStyle.Bottom;
                close.DialogResult = DialogResult.OK;
                notice.Controls.Add(body);
                notice.Controls.Add(close);
                notice.AcceptButton = close;
                notice.CancelButton = close;
                Presentation.Refresh(notice);
                body.BackColor = notice.BackColor;
                notice.Shown += delegate { ApplyWindowTheme(notice.Handle); };
                notice.ShowDialog();
            }
        }

        private void SetDetailsVisible(bool visible)
        {
            detailsPanel.Visible = visible;
            Presentation.SetCopy(detailsButton, visible ? "收起启动详情" : "查看启动详情");
            startupLayout.PerformLayout();
        }

        private void FitToWorkingArea()
        {
            Rectangle workingArea = Screen.FromControl(this).WorkingArea;
            int maxWidth = Math.Max(640, workingArea.Width - 32);
            int maxHeight = Math.Max(460, workingArea.Height - 32);
            MinimumSize = new Size(Math.Min(MinimumSize.Width, maxWidth), Math.Min(MinimumSize.Height, maxHeight));
            int width = Math.Min(Width, maxWidth);
            int height = Math.Min(Height, maxHeight);
            SetBounds(workingArea.Left + (workingArea.Width - width) / 2,
                workingArea.Top + (workingArea.Height - height) / 2, width, height);
        }

        private string PreparationStage(int percent)
        {
            if (!firstLaunch)
            {
                if (percent < 35) return "正在检查本地运行时";
                if (percent < 85) return "正在连接插件与工作区";
                return "Agent 即将醒来";
            }
            if (percent < 10) return "正在核验登机清单";
            if (percent < 38) return "运行时正在装舱";
            if (percent < 78) return "专家正在飞往本地工作区";
            if (percent < 96) return "即将抵达，正在整理最后一排座位";
            return "专家即将落地";
        }

        private static string PreparationDetail(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return "正在准备本地运行时。";
            if (message.IndexOf("verifying bundled runtime", StringComparison.OrdinalIgnoreCase) >= 0)
                return "正在核验整合包中的运行时文件。";
            if (message.IndexOf("unpacking bundled runtime", StringComparison.OrdinalIgnoreCase) >= 0)
                return "正在解压运行时、内置插件与工作区模板。";
            if (message.IndexOf("bundled runtime ready", StringComparison.OrdinalIgnoreCase) >= 0)
                return "本地运行时已经准备完成。";
            return message;
        }

        public bool ValidateStartupLayoutForTest(Size clientSize, out string error)
        {
            ClientSize = clientSize;
            CreateControl();
            PerformLayout();
            startup.PerformLayout();
            startupLayout.PerformLayout();

            Control[] primaryControls = { startupHeading, startupExplanation, progressText, progressBar, startupActions };
            foreach (Control control in primaryControls)
            {
                if (!ControlFitsInside(control, startup, out error)) return false;
            }

            SetDetailsVisible(true);
            PerformLayout();
            startup.PerformLayout();
            startupLayout.PerformLayout();
            if (!ControlFitsInside(output, startup, out error)) return false;
            SetDetailsVisible(false);
            error = null;
            return true;
        }

        public void SetAppearanceForTest(string language, string theme)
        {
            Presentation.Language = language;
            Presentation.Preference = theme;
            Presentation.Theme = theme;
            RefreshAppearance();
            string expected = language == "zh" ? "专家正在登机" : "Experts are boarding";
            if (startupHeading.Text != expected) throw new InvalidOperationException("Startup language did not switch.");
        }

        private static bool ControlFitsInside(Control control, Control ancestor, out string error)
        {
            int x = 0;
            int y = 0;
            Control current = control;
            while (current != null && current != ancestor)
            {
                x += current.Left;
                y += current.Top;
                current = current.Parent;
            }
            if (current != ancestor)
            {
                error = control.Name + " is not inside the startup surface.";
                return false;
            }

            Rectangle bounds = new Rectangle(x, y, control.Width, control.Height);
            Rectangle client = ancestor.ClientRectangle;
            if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.Left < 0 || bounds.Top < 0
                || bounds.Right > client.Right || bounds.Bottom > client.Bottom)
            {
                error = control.GetType().Name + " bounds " + bounds + " exceed startup client " + client + ".";
                return false;
            }
            error = null;
            return true;
        }

        private void StartUpdateAction(string action, bool revealResult)
        {
            if (closing || updateBusy || !ready) return;
            string manifest = Path.Combine(root, "harness", "distribution", "agent-release.json");
            string script = Path.Combine(root, "harness", "scripts", "Check-AgentUpdate.ps1");
            if (!File.Exists(manifest) || !File.Exists(script))
            {
                desktopPhase = "error";
                desktopMessage = Presentation.Translate("The bundled updater is unavailable. Reinstall the complete MoE4All Agent package.");
                PublishDesktopState(revealResult);
                return;
            }
            updateBusy = true;
            updateInstalling = action == "Install";
            updateStatusFailed = false;
            revealUpdateResult = revealResult;
            desktopPhase = updateInstalling ? "download" : "checking";
            desktopMessage = "";
            desktopCompleted = 0;
            desktopTotal = updateInstalling ? availableUpdateBytes : 0;
            PublishDesktopState(revealResult);
            Thread worker = new Thread(new ThreadStart(delegate { RunUpdateAction(action); }));
            worker.IsBackground = true;
            worker.Name = "MoE4All Agent updater";
            worker.Start();
        }

        private void RunUpdateAction(string action)
        {
            StringBuilder errors = new StringBuilder();
            int exitCode = -1;
            try
            {
                string script = Path.Combine(root, "harness", "scripts", "Check-AgentUpdate.ps1");
                ProcessStartInfo start = PowerShellStart(root);
                start.Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File " + Quote(script)
                    + " -InstallRoot " + Quote(root)
                    + " -ParentPid " + Process.GetCurrentProcess().Id
                    + " -Action " + action;
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                start.StandardOutputEncoding = new UTF8Encoding(false);
                start.StandardErrorEncoding = new UTF8Encoding(false);
                using (Process updater = new Process())
                {
                    updater.StartInfo = start;
                    updater.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data != null && !TryHandleUpdateStatus(e.Data)) WriteMessage(e.Data);
                    };
                    updater.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        lock (errors) errors.AppendLine(e.Data);
                        WriteMessage(e.Data, true);
                    };
                    if (!updater.Start()) throw new InvalidOperationException("The updater process could not start.");
                    updater.BeginOutputReadLine();
                    updater.BeginErrorReadLine();
                    updater.WaitForExit();
                    updater.WaitForExit();
                    exitCode = updater.ExitCode;
                }
            }
            catch (Exception error)
            {
                lock (errors) errors.AppendLine(error.Message);
            }

            string detail;
            lock (errors) detail = errors.ToString().Trim();
            if (!closing)
            {
                try { BeginInvoke(new Action<string, int, string>(FinishUpdateAction), action, exitCode, detail); }
                catch (InvalidOperationException) { }
            }
        }

        private bool TryHandleUpdateStatus(string message)
        {
            if (!message.StartsWith(UpdatePrefix, StringComparison.Ordinal)) return false;
            string[] parts = message.Split(new char[] { '\t' }, 5);
            long completed;
            long total;
            if (parts.Length != 5 || !long.TryParse(parts[2], out completed) || !long.TryParse(parts[3], out total)) return true;
            if (!closing)
            {
                try
                {
                    BeginInvoke(new Action<string, long, long, string>(ApplyUpdateStatus),
                        parts[1], completed, total, parts[4]);
                }
                catch (InvalidOperationException) { }
            }
            return true;
        }

        private void ApplyUpdateStatus(string kind, long completed, long total, string message)
        {
            if (IsDisposed) return;
            if (kind == "checking" && updateInstalling) return;
            desktopPhase = kind == "none" ? "current" : kind;
            desktopCompleted = Math.Max(0, completed);
            desktopTotal = Math.Max(0, total);
            desktopMessage = message;
            if (kind == "none") { availableUpdate = null; availableUpdateBytes = 0; }
            if (kind == "available") { availableUpdate = message; availableUpdateBytes = total; }
            if (kind == "error") updateStatusFailed = true;
            PublishDesktopState(kind == "available" || (kind == "error" && (revealUpdateResult || updateInstalling)));
        }
        private void FinishUpdateAction(string action, int exitCode, string error)
        {
            updateBusy = false;
            updateInstalling = false;
            if (exitCode != 0 && !updateStatusFailed)
            {
                desktopPhase = "error";
                desktopMessage = string.IsNullOrEmpty(error) ? "The updater exited before completing." : error;
            }
            PublishDesktopState(exitCode != 0 && revealUpdateResult);
        }

        public void AppendLog(string message, bool isError)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string, bool>(AppendLog), message, isError); }
                catch (InvalidOperationException) { }
                return;
            }
            if (isError) Presentation.SetCopy(status, "Needs attention");
            else if (ready) Presentation.SetCopy(status, "Ready");
            else if (!preparingProgress) Presentation.SetCopy(status, "Starting");
            if (isError) status.ForeColor = Presentation.ColorFor("error");
            output.AppendText(message + Environment.NewLine);
            if (bootstrapFailed) PushStartupState();
        }

        public void UpdatePreparationProgress(long completed, long total, string message)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<long, long, string>(UpdatePreparationProgress), completed, total, message); }
                catch (InvalidOperationException) { }
                return;
            }
            preparingProgress = true;
            int value = total <= 0 ? 0 : (int)Math.Max(0, Math.Min(1000, completed * 1000 / total));
            int percent = value / 10;
            progressBar.SetProgress(value);
            Presentation.SetCopy(progressText, PreparationStage(percent));
            Presentation.SetCopy(progressDetail, PreparationDetail(message));
            Presentation.SetCopy(progressPercent, percent + "%");
            Presentation.SetCopy(status, "Preparing " + percent + "%");
            status.ForeColor = Presentation.ColorFor("muted");
            PushStartupState();
        }

        private void CompletePreparation()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(CompletePreparation)); }
                catch (InvalidOperationException) { }
                return;
            }
            preparingProgress = false;
            Presentation.SetCopy(progressText, firstLaunch ? "专家已落地，正在唤醒 Agent" : "本地环境就绪，正在唤醒 Agent");
            Presentation.SetCopy(progressDetail, "正在启动桌面界面与本地服务。");
            Presentation.SetCopy(progressPercent, "启动中");
            progressBar.SetIndeterminate();
            Presentation.SetCopy(status, "Starting");
            PushStartupState();
        }

        private void StartAgent()
        {
            Thread worker = new Thread(RunAgent);
            worker.IsBackground = true;
            worker.Name = "MoE4All Agent launcher";
            worker.Start();
        }

        private void RunAgent()
        {
            string state = Path.Combine(root, "data");
            string profile = Path.Combine(state, "home", "profiles", "web", "node_modules");
            string script = Path.Combine(root, "harness", "scripts", "Run-Agent.ps1");
            string prepareScript = Path.Combine(root, "harness", "scripts", "Prepare-BundledRuntime.ps1");
            if (!File.Exists(script) || !File.Exists(prepareScript))
            {
                Fail("The bundled Agent runtime is incomplete. Install the full MoE4All package, not MoE4All.exe alone.");
                return;
            }

            try
            {
                job = CreateChildJob();
                bridge = new DesktopBridge(this, Path.Combine(root, "models"));
                bridge.Start();
                if (!Directory.Exists(profile)) WriteMessage("Preparing the bundled MoE4All Agent runtime.");
                int prepared = RunScript(job, root, prepareScript,
                    " -InstallRoot " + Quote(root) + " -StateRoot " + Quote(state));
                if (prepared != 0 || !Directory.Exists(profile))
                {
                    Fail("Preparing the bundled Agent runtime failed with code " + prepared + ".");
                    return;
                }

                CompletePreparation();
                WriteMessage("MoE4All Agent is starting.");
                Thread readiness = new Thread(WaitForReady);
                readiness.IsBackground = true;
                readiness.Start();
                int result = RunScript(job, root, script,
                    " -StateRoot " + Quote(state)
                    + " -DesktopBridge " + Quote(bridge.BaseUrl)
                    + " -DesktopToken " + Quote(bridge.Token));
                if (!closing) Fail("MoE4All Agent exited with code " + result + ".");
            }
            catch (Exception error)
            {
                if (!closing) Fail("MoE4All Agent could not start: " + error.Message);
                WriteMessage(error.ToString(), true);
            }
        }

        private void WaitForReady()
        {
            for (int attempt = 0; attempt < 600 && !closing; attempt++)
            {
                try
                {
                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + WebPort + "/");
                    request.Timeout = 1000;
                    request.ReadWriteTimeout = 1000;
                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 400)
                        {
                            BeginInvoke(new Action(OnAgentReady));
                            return;
                        }
                    }
                }
                catch { }
                Thread.Sleep(500);
            }
            if (!closing) Fail("The Agent web service did not become ready. See the startup log for details.");
        }

        private void OnAgentReady()
        {
            ready = true;
            Presentation.SetCopy(status, "Ready");
            status.ForeColor = Presentation.ColorFor("success");
            status.Visible = false;
            ActivateWebView();
            if (File.Exists(Path.Combine(root, "harness", "distribution", "agent-release.json"))) StartUpdateAction("Check", false);
        }

        private Task InitializeBrowser()
        {
            if (browserInitialization == null) browserInitialization = InitializeBrowserCore();
            return browserInitialization;
        }
        private async Task InitializeBrowserCore()
        {
            try
            {
                string userData = Path.Combine(root, "cache", "webview2");
                Directory.CreateDirectory(userData);
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, userData);
                await webView.EnsureCoreWebView2Async(environment);
                webView.CoreWebView2.WebMessageReceived += ReceiveAppearance;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; OpenUrl(e.Uri); };
                webView.CoreWebView2.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                {
                    Uri uri;
                    if (Uri.TryCreate(e.Uri, UriKind.Absolute, out uri) && e.Uri != "about:blank"
                        && !(uri.Scheme == "http" && uri.Host == "127.0.0.1" && uri.Port == WebPort))
                    { e.Cancel = true; if (uri.Scheme == "https" || uri.Scheme == "http") OpenUrl(e.Uri); }
                };
                webView.CoreWebView2.NavigationCompleted += delegate { if (!ready) { bootstrapLoaded = true; PushStartupState(); } else PublishDesktopState(false); };
                ShowBootstrap();
            }
            catch (Exception error)
            {
                runtimeButton.Visible = true;
                WriteMessage("The desktop view is unavailable. Install WebView2 or use Open in browser. " + error.Message, true);
            }
        }
        private void ShowBootstrap()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MoE4All.Startup.html"))
            using (StreamReader reader = new StreamReader(stream)) webView.CoreWebView2.NavigateToString(reader.ReadToEnd().Replace("/*INITIAL_STATE*/", "window.setState(" + StartupStateJson() + ");"));
            startup.Visible = false;
            webView.Visible = true;
            webView.BringToFront();
        }
        private async void ActivateWebView()
        {
            await InitializeBrowser();
            if (closing || webView.CoreWebView2 == null) return;
            bootstrapLoaded = false;
            desktopBridgeReady = false;
            webView.Source = new Uri("http://127.0.0.1:" + WebPort + "/");
        }
        private async void PushStartupState()
        {
            if (ready || !bootstrapLoaded || webView.CoreWebView2 == null) return;
            try { await webView.CoreWebView2.ExecuteScriptAsync("window.setState(" + StartupStateJson() + ")"); }
            catch (Exception error) { if (!closing) WriteMessage("Could not refresh startup view: " + error.Message); }
        }

        private string StartupStateJson()
        {
            var colors = new Dictionary<string, string>();
            foreach (string role in new string[] { "base", "surface", "text", "muted", "faint", "border", "accent", "error" })
                colors[role] = ColorTranslator.ToHtml(Presentation.ColorFor(role));
            var value = new {
                language = Presentation.Language, theme = Presentation.Theme, colors = colors,
                first = firstLaunch, failed = bootstrapFailed, heading = startupHeading.Text,
                explanation = startupExplanation.Text, stage = progressText.Text, detail = progressDetail.Text,
                percent = progressPercent.Text, progress = preparingProgress ? (object)progressBar.ProgressValue : null,
                log = output.Text.Length > 16000 ? output.Text.Substring(output.Text.Length - 16000) : output.Text,
                detailsLabel = Presentation.Translate("查看启动详情"), logLabel = Presentation.Translate("打开启动日志"),
                browserLabel = Presentation.Translate("Open in browser")
            };
            return new JavaScriptSerializer().Serialize(value);
        }

        public void ExportBootstrapForTest(string directory, string name)
        {
            File.WriteAllText(Path.Combine(directory, name + ".json"), StartupStateJson(), new UTF8Encoding(false));
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MoE4All.Startup.html"))
            using (StreamReader reader = new StreamReader(stream)) File.WriteAllText(Path.Combine(directory, "startup.html"), reader.ReadToEnd(), new UTF8Encoding(false));
            int actual;
            if (DwmGetWindowAttribute(Handle, 35, out actual, 4) == 0 && actual != ColorTranslator.ToWin32(Presentation.ColorFor("base")))
                throw new InvalidOperationException("The native title bar does not match the selected palette.");
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr handle, int attribute, out int value, int size);

        private void Fail(string message)
        {
            WriteMessage(message, true);
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(FailUi), message); }
                catch (InvalidOperationException) { }
            }
            else FailUi(message);
        }

        private void FailUi(string message)
        {
            ready = false;
            bootstrapFailed = true;
            preparingProgress = false;
            Presentation.SetCopy(status, "Needs attention");
            status.ForeColor = Presentation.ColorFor("error");
            Presentation.SetCopy(startupHeading, "启动遇到问题");
            Presentation.SetCopy(startupExplanation, "MoE4All 没有继续运行。具体原因已展开在下方，修正后重新启动即可。");
            progressBar.SetFailed();
            Presentation.SetCopy(progressText, "需要处理后再继续");
            Presentation.SetCopy(progressDetail, message);
            Presentation.SetCopy(progressPercent, "已停止");
            SetDetailsVisible(true);
            if (webView.CoreWebView2 != null) { ShowBootstrap(); PushStartupState(); }
            else { startup.Visible = true; startup.BringToFront(); }
            browserButton.Enabled = true;
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            closing = true;
            SystemEvents.UserPreferenceChanged -= OnSystemAppearanceChanged;
            toolbarTip.Dispose();
            if (bridge != null) bridge.Dispose();
            IntPtr current = job;
            job = IntPtr.Zero;
            if (current != IntPtr.Zero) CloseHandle(current);
        }

        public string PickDirectory()
        {
            if (InvokeRequired) return (string)Invoke(new Func<string>(PickDirectory));
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = Presentation.Translate("Select a MoE4All workspace directory");
                dialog.ShowNewFolderButton = true;
                string workspace = Path.Combine(root, "data", "workspace");
                if (Directory.Exists(workspace)) dialog.SelectedPath = workspace;
                return ShowThemedPicker(dialog) == DialogResult.OK ? dialog.SelectedPath : null;
            }
        }

        public string PickModel(string initialDirectory)
        {
            if (InvokeRequired) return (string)Invoke(new Func<string, string>(PickModel), initialDirectory);
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = Presentation.Translate("Select a MoE4All GGUF model");
                dialog.Filter = Presentation.Translate("GGUF model (*.gguf)|*.gguf|All files (*.*)|*.*");
                dialog.Multiselect = false;
                dialog.CheckFileExists = true;
                dialog.InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : root;
                return ShowThemedPicker(dialog) == DialogResult.OK ? dialog.FileName : null;
            }
        }

        public string PickEngine()
        {
            if (InvokeRequired) return (string)Invoke(new Func<string>(PickEngine));
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = Presentation.Translate("Select a MoE4All engine package or infr.exe");
                dialog.Filter = "MoE4All engine (*.zip;infr.exe)|*.zip;infr.exe|ZIP archive (*.zip)|*.zip|Engine executable (infr.exe)|infr.exe|All files (*.*)|*.*";
                dialog.Multiselect = false;
                dialog.CheckFileExists = true;
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                dialog.InitialDirectory = Directory.Exists(downloads) ? downloads : root;
                return ShowThemedPicker(dialog) == DialogResult.OK ? dialog.FileName : null;
            }
        }

        private delegate bool WindowVisitor(IntPtr handle, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint thread, WindowVisitor visitor, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr handle, StringBuilder name, int capacity);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        private DialogResult ShowThemedPicker(CommonDialog dialog)
        {
            uint thread = GetCurrentThreadId();
            using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer())
            {
                timer.Interval = 80;
                timer.Tick += delegate
                {
                    EnumThreadWindows(thread, delegate(IntPtr handle, IntPtr parameter)
                    {
                        StringBuilder name = new StringBuilder(64);
                        GetClassName(handle, name, name.Capacity);
                        if (name.ToString() == "#32770") ApplyWindowTheme(handle);
                        return true;
                    }, IntPtr.Zero);
                };
                timer.Start();
                return dialog.ShowDialog(this);
            }
        }
    }

    private sealed class DesktopBridge : IDisposable
    {
        private readonly AgentWindow owner;
        private readonly string modelDirectory;
        private readonly TcpListener listener;
        private Thread thread;
        private volatile bool stopping;
        public readonly string Token;
        public string BaseUrl { get; private set; }

        public DesktopBridge(AgentWindow windowOwner, string models)
        {
            owner = windowOwner;
            modelDirectory = models;
            listener = new TcpListener(IPAddress.Loopback, 0);
            byte[] secret = new byte[32];
            using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(secret);
            Token = Convert.ToBase64String(secret);
        }

        public void Start()
        {
            listener.Start();
            IPEndPoint endpoint = (IPEndPoint)listener.LocalEndpoint;
            BaseUrl = "http://127.0.0.1:" + endpoint.Port;
            thread = new Thread(Listen);
            thread.IsBackground = true;
            thread.Name = "MoE4All desktop picker bridge";
            thread.Start();
        }

        private void Listen()
        {
            while (!stopping)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(delegate { HandleSafely(client); });
                }
                catch (SocketException) { if (!stopping) WriteMessage("Desktop bridge stopped unexpectedly.", true); }
                catch (ObjectDisposedException) { }
            }
        }

        private void HandleSafely(TcpClient client)
        {
            try
            {
                Handle(client);
            }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is SocketException) && !(error is ObjectDisposedException) && !stopping)
                    WriteMessage("Desktop bridge request failed: " + error.Message, true);
            }
        }

        private void Handle(TcpClient client)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                stream.ReadTimeout = 5000;
                string headers = ReadHeaders(stream);
                string[] lines = headers.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                string[] request = lines.Length == 0 ? new string[0] : lines[0].Split(' ');
                string method = request.Length > 0 ? request[0] : "";
                string path = request.Length > 1 ? request[1] : "";
                string supplied = "";
                for (int index = 1; index < lines.Length; index++)
                {
                    int separator = lines[index].IndexOf(':');
                    if (separator > 0 && lines[index].Substring(0, separator).Trim().Equals("X-MoE4All-Token", StringComparison.OrdinalIgnoreCase))
                        supplied = lines[index].Substring(separator + 1).Trim();
                }
                if (method != "POST" || supplied != Token)
                {
                    Respond(stream, 403, "{\"error\":\"forbidden\"}");
                    return;
                }
                string selected;
                if (path == "/pick-directory") selected = owner.PickDirectory();
                else if (path == "/pick-model") selected = owner.PickModel(modelDirectory);
                else if (path == "/pick-engine") selected = owner.PickEngine();
                else
                {
                    Respond(stream, 404, "{\"error\":\"not-found\"}");
                    return;
                }
                Respond(stream, 200, "{\"path\":" + (selected == null ? "null" : "\"" + JsonEscape(selected) + "\"") + "}");
            }
        }

        private static string ReadHeaders(NetworkStream stream)
        {
            MemoryStream buffer = new MemoryStream();
            int matched = 0;
            while (buffer.Length < 16384)
            {
                int value = stream.ReadByte();
                if (value < 0) break;
                buffer.WriteByte((byte)value);
                if ((matched == 0 && value == 13) || (matched == 1 && value == 10)
                    || (matched == 2 && value == 13) || (matched == 3 && value == 10))
                {
                    matched++;
                    if (matched == 4) break;
                }
                else matched = value == 13 ? 1 : 0;
            }
            return Encoding.ASCII.GetString(buffer.ToArray());
        }

        private static string JsonEscape(string value)
        {
            StringBuilder result = new StringBuilder();
            foreach (char character in value)
            {
                if (character == '\\') result.Append("\\\\");
                else if (character == '\"') result.Append("\\\"");
                else if (character == '\r') result.Append("\\r");
                else if (character == '\n') result.Append("\\n");
                else if (character < 32) result.Append("\\u" + ((int)character).ToString("x4"));
                else result.Append(character);
            }
            return result.ToString();
        }

        private static void Respond(NetworkStream stream, int status, string body)
        {
            byte[] payload = new UTF8Encoding(false).GetBytes(body);
            string reason = status == 200 ? "OK" : (status == 403 ? "Forbidden" : "Not Found");
            string headers = "HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: "
                + payload.Length + "\r\nConnection: close\r\n\r\n";
            byte[] prefix = Encoding.ASCII.GetBytes(headers);
            stream.Write(prefix, 0, prefix.Length);
            stream.Write(payload, 0, payload.Length);
        }

        public void Dispose()
        {
            stopping = true;
            try { listener.Stop(); }
            catch { }
        }
    }
}
