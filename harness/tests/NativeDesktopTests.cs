using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal static class NativeDesktopTests
{
    internal static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    [STAThread]
    private static int Main(string[] args)
    {
        try {
            Application.EnableVisualStyles();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.SetCompatibleTextRenderingDefault(false);
            string area = args.Length > 0 ? args[0] : "picker";
            if (area == "picker") Picker(args.Length > 1 && args[1] == "--dialogs");
            else if (area == "chrome") MoE4All.ValidateNativeChrome(Environment.CurrentDirectory);
            else if (area == "chrome-live") MoE4All.ValidateLiveChrome(Environment.CurrentDirectory);
            else if (area == "startup-flow") MoE4All.ValidateStartupFlow(Environment.CurrentDirectory);
            else if (area == "picker-light") ProbeDialog("white", "directory");
            else throw new ArgumentException("Unknown native test area.");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Picker(bool dialogs)
    {
        foreach (string kind in new string[] { "directory", "model", "engine" }) {
            uint options = MoE4All.NativePicker.Options(kind);
            Check((options & 0x40) != 0 && (options & 0x800) != 0 && (options & 8) != 0, "Filesystem, existing paths and unchanged CWD required.");
            Check(((options & 0x20) != 0) == (kind == "directory"), "Only folder mode sets PICKFOLDERS.");
            Check(((options & 0x1000) != 0) == (kind != "directory"), "File mode requires existing files.");
            MoE4All.NativePicker.IFileDialog dialog = MoE4All.NativePicker.Create(kind, Environment.CurrentDirectory);
            try {
                Check(Marshal.IsComObject(dialog), "Must use the Windows Common Item Dialog.");
                uint actual; dialog.GetOptions(out actual);
                Check((actual & options) == options, "COM options differ: " + actual.ToString("x") + " vs " + options.ToString("x"));
                MoE4All.NativePicker.IShellItem folder;
                dialog.GetFolder(out folder);
                try {
                    IntPtr name; folder.GetDisplayName(0x80058000, out name);
                    try { Check(Path.GetFullPath(Marshal.PtrToStringUni(name)) == Path.GetFullPath(Environment.CurrentDirectory), "Initial directory lost."); }
                    finally { Marshal.FreeCoTaskMem(name); }
                } finally { Marshal.ReleaseComObject(folder); }
                bool rejected = false;
                try { MoE4All.NativePicker.Show(dialog, IntPtr.Zero, kind); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "An ownerless file dialog must not be opened.");
            } finally { Marshal.ReleaseComObject(dialog); }
        }
        Check(MoE4All.PickerFileAllowed("model", @"C:\模型\a.GGUF"), "Case-insensitive GGUF filter.");
        Check(!MoE4All.PickerFileAllowed("model", "weights.exe"), "Reject invalid models.");
        Check(MoE4All.PickerFileAllowed("engine", "infr.exe") && MoE4All.PickerFileAllowed("engine", "engine.ZIP"), "Allow engine ZIP and infr.exe.");
        Check(!MoE4All.PickerFileAllowed("engine", "other.exe"), "Reject unrelated executables.");
        foreach (string theme in new string[] { "green", "gray", "black" }) Check(NativeDialogTheme.Mode(theme, false) == 2, "Dark palette must request native dark mode.");
        Check(NativeDialogTheme.Mode("white", false) == 3, "White palette must request native light mode.");
        Check(NativeDialogTheme.Mode("black", true) == 0, "Preserve Windows high contrast.");
        if (dialogs) {
            foreach (string theme in new string[] { "white", "gray" })
                foreach (string kind in new string[] { "directory", "model", "engine" }) ProbeDialog(theme, kind);
            foreach (string kind in new string[] { "directory", "model", "engine" }) ProbeDialog("gray", kind, true);
        }
        Console.WriteLine("Picker passed: modern COM, all three modes, initial folder, filters, owner requirement, dark/light/high-contrast policy" + (dialogs ? ", owned modal Explorer dialogs, foreground restore, selection and cancellation." : "."));
    }

    private static void ProbeDialog(string theme, string kind, bool accept = false)
    {
        using (Form owner = new Form())
        using (Timer cancel = new Timer()) {
            owner.ShowInTaskbar = false;
            owner.Show();
            bool wasMaximized = accept && kind == "directory";
            if (wasMaximized) owner.WindowState = FormWindowState.Maximized;
            owner.WindowState = FormWindowState.Minimized;
            MoE4All.NativePicker.PrepareOwner(owner);
            Check(owner.WindowState != FormWindowState.Minimized && !owner.TopMost, "Selection restores its own window without making it globally topmost.");
            if (wasMaximized) Check(owner.WindowState == FormWindowState.Maximized, "Restoring for selection must preserve the previous maximized state.");
            Console.WriteLine("Opening " + theme + " " + kind + " Explorer dialog (" + (accept ? "select" : "cancel") + ").");
            using (NativeDialogTheme colors = new NativeDialogTheme(owner.Handle, theme)) {
            MoE4All.NativePicker.IFileDialog dialog = MoE4All.NativePicker.Create(kind, Environment.CurrentDirectory);
            string expected = kind == "directory" ? Environment.CurrentDirectory : Path.Combine(Environment.CurrentDirectory, kind == "model" ? "fixture.GGUF" : "infr.exe");
            if (accept) {
                if (kind != "directory") File.WriteAllText(expected, "Picker fixture; not an executable or model.");
                if (kind != "directory") dialog.SetFileName(Path.GetFileName(expected));
            }
            bool owned = false, darkMatches = false;
            cancel.Interval = 600;
            int ticks = 0;
            cancel.Tick += delegate {
                ticks++;
                IntPtr popup = IntPtr.Zero;
                EnumThreadWindows(GetCurrentThreadId(), delegate(IntPtr handle, IntPtr state) {
                    StringBuilder name = new StringBuilder(100);
                    GetClassName(handle, name, name.Capacity);
                    if (name.ToString() == "#32770" && GetWindow(handle, 4) == owner.Handle) popup = handle;
                    return true;
                }, IntPtr.Zero);
                owned = popup != IntPtr.Zero && popup != owner.Handle && GetWindow(popup, 4) == owner.Handle;
                if (owned) Check(!IsWindowEnabled(owner.Handle) && IsWindowVisible(popup), "Picker must be a visible modal above all content in its disabled owner.");
                int dark;
                int hr = DwmGetWindowAttribute(popup, 20, out dark, 4);
                darkMatches = hr != 0 || dark == (theme == "white" ? 0 : 1);
                if (owned || ticks >= 5) {
                    cancel.Stop();
                    if (owned) {
                        Rect bounds; GetWindowRect(popup, out bounds);
                        using (Bitmap capture = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top)) {
                            using (Graphics graphics = Graphics.FromImage(capture)) graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, capture.Size);
                            capture.Save(Path.Combine(Environment.CurrentDirectory, theme + "-" + kind + ".png"), ImageFormat.Png);
                        }
                    }
                    if (accept && owned) PostMessage(GetDlgItem(popup, 1), 0xf5, IntPtr.Zero, IntPtr.Zero);
                    else MoE4All.NativePicker.CancelOwnedDialog(owner.Handle);
                }
            };
            try {
                cancel.Start();
                string selected = MoE4All.NativePicker.Show(dialog, owner.Handle, kind);
                Check(accept ? selected == Path.GetFullPath(expected) : selected == null, "Selection/cancellation returned an incorrect path.");
                Check(owned, "Dialog must be owned by the Agent, not the desktop or Node.");
                Check(darkMatches, "Native dialog title bar must follow the selected light/dark mode.");
            } finally { cancel.Stop(); Marshal.ReleaseComObject(dialog); }
            }
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern IntPtr GetDlgItem(IntPtr window, int id);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
    private delegate bool EnumWindow(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, IntPtr state);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder value, int count);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}

internal static partial class MoE4All
{
    internal static void ValidateStartupFlow(string root)
    {
        string node = Environment.GetEnvironmentVariable("MOE4ALL_TEST_NODE");
        NativeDesktopTests.Check(File.Exists(node), "A pinned test Node runtime is required.");
        File.Copy(node, Path.Combine(root, "node.exe"), true);
        string scripts = Path.Combine(root, "harness", "scripts");
        string capabilities = Path.Combine(root, "harness", "capabilities");
        Directory.CreateDirectory(scripts); Directory.CreateDirectory(capabilities);
        File.WriteAllText(Path.Combine(scripts, "Prepare-BundledRuntime.ps1"),
            "param([string]$InstallRoot,[string]$StateRoot)\r\n[IO.File]::AppendAllText((Join-Path $InstallRoot 'prepare-count.txt'),'x')\r\nNew-Item -ItemType Directory -Path (Join-Path $StateRoot 'home/profiles/web/node_modules') -Force | Out-Null\r\nexit 0\r\n");
        File.WriteAllText(Path.Combine(scripts, "Run-Agent.ps1"),
            "param([string]$StateRoot,[string]$DesktopBridge,[string]$DesktopToken)\r\n[IO.File]::WriteAllText((Join-Path $StateRoot 'service-fixture.txt'),'stub; no server or backend')\r\nexit 0\r\n");
        string marker = Path.Combine(root, "data", "service-fixture.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(marker));
        foreach (string value in new string[] { "false", "true", "missing", "wrong-type", "error" }) {
            File.Delete(Path.Combine(root, "prepare-count.txt")); File.Delete(marker);
            string payload = value == "missing" ? "{}" : "{\"profileRepairRequired\":" + (value == "wrong-type" ? "\"false\"" : value == "error" ? "false" : value) + "}";
            File.WriteAllText(Path.Combine(capabilities, "cli.cjs"),
                "process.stdout.write('@@MOE4ALL_CAPABILITIES\\t' + " + new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(payload) + " + '\\n');process.exitCode=" + (value == "error" ? "1" : "0") + ";\n");
            using (AgentWindow window = new AgentWindow(root, false)) {
                Exception failure = null;
                window.Shown += delegate { window.BeginInvoke(new Action(delegate {
                    try { window.ValidateStartupFlowForTest(); }
                    catch (Exception error) { failure = error; }
                    finally { window.Close(); }
                })); };
                Application.Run(window);
                if (failure != null) throw failure;
            }
            int expected = value == "false" || value == "error" ? 1 : 2;
            NativeDesktopTests.Check(File.ReadAllText(Path.Combine(root, "prepare-count.txt")).Length == expected, "Unexpected runtime prepare count for " + value);
            NativeDesktopTests.Check(File.Exists(marker) == (value != "error"), "Failed plugin recovery must not start the service.");
            Console.WriteLine("Startup flow " + value + ": preparations=" + expected + ", service=" + File.Exists(marker));
        }
        Console.WriteLine("Startup flow passed: ordinary startup, update repair, legacy fallback, invalid protocol and fail-closed recovery. Only stub scripts ran.");
    }

    internal static void ValidateLiveChrome(string root)
    {
        Exception failure = null;
        using (AgentWindow window = new AgentWindow(root, false)) {
            window.Shown += delegate { window.BeginInvoke(new Action(delegate {
                try { window.ValidateLiveChromeForTest(); }
                catch (Exception error) { failure = error; }
                finally { window.Close(); }
            })); };
            Application.Run(window);
        }
        if (failure != null) throw failure;
    }

    private sealed partial class AgentWindow
    {
        internal void ValidateStartupFlowForTest()
        {
            startupWatch.Start();
            var task = System.Threading.Tasks.Task.Run(new Action(RunAgent));
            PumpUntil(delegate { return task.IsCompleted; }); task.GetAwaiter().GetResult();
            Application.DoEvents();
        }

        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

        private static T Pump<T>(System.Threading.Tasks.Task<T> task)
        {
            PumpUntil(delegate { return task.IsCompleted; });
            return task.GetAwaiter().GetResult();
        }

        private static void PumpUntil(Func<bool> done)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (!done()) {
                if (watch.ElapsedMilliseconds > 10000) throw new TimeoutException("Live chrome fixture timed out.");
                Application.DoEvents(); System.Threading.Thread.Sleep(10);
            }
        }

        private void MovePointer(Point local, bool drag)
        {
            Activate(); Application.DoEvents();
            NativeDesktopTests.Check(GetForegroundWindow() == Handle, "Only the isolated test window may receive mouse input.");
            Point from = PointToScreen(local);
            Exception failure = null;
            var worker = new System.Threading.Thread(delegate() {
                try {
                    SetCursorPos(from.X, from.Y);
                    mouse_event(2, 0, 0, 0, UIntPtr.Zero);
                    System.Threading.Thread.Sleep(120);
                    if (drag) {
                        for (int step = 1; step <= 6; step++) {
                            SetCursorPos(from.X + step * 10, from.Y + step * 5);
                            System.Threading.Thread.Sleep(40);
                        }
                    }
                } catch (Exception error) { failure = error; }
                finally { mouse_event(4, 0, 0, 0, UIntPtr.Zero); }
            });
            worker.IsBackground = true; worker.Start();
            PumpUntil(delegate { return !worker.IsAlive; });
            if (failure != null) throw failure;
            Application.DoEvents();
        }

        internal void ValidateLiveChromeForTest()
        {
            Show(); WindowState = FormWindowState.Normal;
            Bounds = new Rectangle(100, 100, 1100, 800);
            Application.DoEvents(); LayoutDesktopChrome();
            Rectangle before = Bounds;
            MovePointer(new Point(400, 18), true);
            NativeDesktopTests.Check(Left >= before.Left + 30 && Top >= before.Top + 15, "Native startup surface must drag the window.");
            var environment = Pump(Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null,
                Path.Combine(root, "webview-fixture"), new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions("--disable-gpu")));
            var initialize = webView.EnsureCoreWebView2Async(environment);
            PumpUntil(delegate { return initialize.IsCompleted; }); initialize.GetAwaiter().GetResult();
            var chrome = InitializeDesktopChrome();
            PumpUntil(delegate { return chrome.IsCompleted; }); chrome.GetAwaiter().GetResult();
            bool navigated = false;
            webView.CoreWebView2.NavigationCompleted += delegate { navigated = true; };
            webView.CoreWebView2.NavigateToString("<!doctype html><html><head></head><body style='background:#303530;color:white;margin:0'><button id='action' style='position:absolute;left:180px;top:8px' onclick='window.clicks++'>Action</button><script>window.clicks=0</script><p style='margin-top:60px'>Isolated chrome fixture; no Agent or backend service.</p></body></html>");
            PumpUntil(delegate { return navigated; });
            startup.Visible = false; webView.Visible = true; webView.BringToFront(); LayoutDesktopChrome();
            NativeDesktopTests.Check(Pump(webView.CoreWebView2.ExecuteScriptAsync("!!document.getElementById('moe4all-native-drag-region')")) == "true", "Document-created script must install its drag surface.");
            before = Bounds;
            MovePointer(new Point(400, 18), true);
            NativeDesktopTests.Check(Left >= before.Left + 30 && Top >= before.Top + 15, "Blank WebView top area must drag the native window.");
            before = Bounds;
            Console.WriteLine("Live button geometry: dpi=" + DeviceDpi + ", zoom=" + webView.ZoomFactor + ", " + Pump(webView.CoreWebView2.ExecuteScriptAsync("JSON.stringify({rect:document.getElementById('action').getBoundingClientRect().toJSON(),region:getComputedStyle(document.getElementById('action')).webkitAppRegion})")));
            MovePointer(new Point(205, 18), false);
            System.Threading.Thread.Sleep(150); Application.DoEvents();
            NativeDesktopTests.Check(Pump(webView.CoreWebView2.ExecuteScriptAsync("window.clicks")) == "1", "Top-row interactive controls must receive clicks.");
            NativeDesktopTests.Check(Bounds == before, "Clicking a top-row control must not move the window.");
            Console.WriteLine("Live chrome passed: actual startup and WebView dragging, document-created installation, clickable top-row button. No Agent/backend started.");
            Close();
        }
    }
}
