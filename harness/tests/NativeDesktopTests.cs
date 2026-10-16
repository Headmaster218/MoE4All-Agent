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
            Application.SetCompatibleTextRenderingDefault(false);
            string area = args.Length > 0 ? args[0] : "picker";
            if (area == "picker") Picker(args.Length > 1 && args[1] == "--dialogs");
            else if (area == "chrome") MoE4All.ValidateNativeChrome(Environment.CurrentDirectory);
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
        if (dialogs) foreach (string theme in new string[] { "white", "gray" })
            foreach (string kind in new string[] { "directory", "model", "engine" }) ProbeDialog(theme, kind);
        Console.WriteLine("Picker passed: modern COM, all three modes, initial folder, filters, owner requirement, dark/light/high-contrast policy" + (dialogs ? ", owned themed Explorer dialogs and cancellation." : "."));
    }

    private static void ProbeDialog(string theme, string kind)
    {
        using (Form owner = new Form())
        using (Timer cancel = new Timer()) {
            owner.ShowInTaskbar = false;
            owner.Show();
            Console.WriteLine("Opening " + theme + " " + kind + " Explorer dialog.");
            using (NativeDialogTheme colors = new NativeDialogTheme(owner.Handle, theme)) {
            MoE4All.NativePicker.IFileDialog dialog = MoE4All.NativePicker.Create(kind, Environment.CurrentDirectory);
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
                    MoE4All.NativePicker.CancelOwnedDialog(owner.Handle);
                }
            };
            try {
                cancel.Start();
                Check(MoE4All.NativePicker.Show(dialog, owner.Handle, kind) == null, "Cancellation must return no path.");
                Check(owned, "Dialog must be owned by the Agent, not the desktop or Node.");
                Check(darkMatches, "Native dialog title bar must follow the selected light/dark mode.");
            } finally { cancel.Stop(); Marshal.ReleaseComObject(dialog); }
            }
        }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect bounds);
    private delegate bool EnumWindow(IntPtr window, IntPtr state);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, IntPtr state);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder value, int count);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
