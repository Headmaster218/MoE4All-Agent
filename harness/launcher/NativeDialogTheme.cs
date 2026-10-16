using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal sealed class NativeDialogTheme : IDisposable
{
    private delegate int PreferredMode(int mode);
    private delegate bool AllowWindow(IntPtr window, bool allow);
    private delegate void RefreshTheme();
    private delegate bool EnumWindow(IntPtr window, IntPtr state);
    private readonly PreferredMode setMode;
    private readonly AllowWindow allowWindow;
    private readonly RefreshTheme refresh, flush;
    private readonly IntPtr owner, library;
    private readonly Timer timer;
    private readonly bool dark;
    private int previous;
    private IntPtr themedWindow;

    internal static int Mode(string theme, bool highContrast) { return highContrast ? 0 : theme == "white" ? 3 : 2; }

    public NativeDialogTheme(IntPtr window, string theme)
    {
        owner = window; dark = theme != "white";
        // Optional private Windows APIs: only bind their modern signatures on supported builds.
        FileVersionInfo windowsTheme = FileVersionInfo.GetVersionInfo(Path.Combine(Environment.SystemDirectory, "uxtheme.dll"));
        if (!SystemInformation.HighContrast && windowsTheme.FileMajorPart >= 10 && windowsTheme.FileBuildPart >= 18362) {
            library = LoadLibraryEx("uxtheme.dll", IntPtr.Zero, 0x800);
            if (library != IntPtr.Zero) {
                IntPtr mode = GetProcAddress(library, new IntPtr(135)), allow = GetProcAddress(library, new IntPtr(133));
                if (mode != IntPtr.Zero) {
                    setMode = (PreferredMode)Marshal.GetDelegateForFunctionPointer(mode, typeof(PreferredMode));
                    previous = setMode(Mode(theme, false));
                    IntPtr refreshAddress = GetProcAddress(library, new IntPtr(104)), flushAddress = GetProcAddress(library, new IntPtr(136));
                    if (refreshAddress != IntPtr.Zero) refresh = (RefreshTheme)Marshal.GetDelegateForFunctionPointer(refreshAddress, typeof(RefreshTheme));
                    if (flushAddress != IntPtr.Zero) flush = (RefreshTheme)Marshal.GetDelegateForFunctionPointer(flushAddress, typeof(RefreshTheme));
                    if (refresh != null) refresh();
                    if (flush != null) flush();
                }
                if (allow != IntPtr.Zero) allowWindow = (AllowWindow)Marshal.GetDelegateForFunctionPointer(allow, typeof(AllowWindow));
            }
        }
        timer = new Timer();
        timer.Interval = 50;
        timer.Tick += delegate { EnumThreadWindows(GetCurrentThreadId(), OnDialog, IntPtr.Zero); };
        timer.Start();
    }

    private bool OnDialog(IntPtr window, IntPtr data)
    {
        StringBuilder name = new StringBuilder(32);
        if (window != themedWindow && GetWindow(window, 4) == owner
            && GetClassName(window, name, name.Capacity) != 0 && name.ToString() == "#32770") {
            themedWindow = window;
            int value = dark && !SystemInformation.HighContrast ? 1 : 0;
            if (allowWindow != null) allowWindow(window, value != 0);
            DwmSetWindowAttribute(window, 20, ref value, 4);
        }
        return true;
    }

    public void Dispose()
    {
        timer.Stop(); timer.Dispose();
        if (setMode != null) setMode(previous);
        if (refresh != null) refresh();
        if (flush != null) flush();
        if (library != IntPtr.Zero) FreeLibrary(library);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibraryEx(string name, IntPtr file, uint flags);
    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress")] private static extern IntPtr GetProcAddress(IntPtr library, IntPtr ordinal);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr library);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, IntPtr state);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder value, int count);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
