using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static partial class MoE4All
{
    internal static bool PickerFileAllowed(string kind, string file)
    {
        return kind == "model" ? Path.GetExtension(file).Equals(".gguf", StringComparison.OrdinalIgnoreCase)
            : kind == "engine" && (Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals("infr.exe", StringComparison.OrdinalIgnoreCase));
    }

    internal static class NativePicker
    {
        internal const int Cancelled = unchecked((int)0x800704c7);
        private delegate bool EnumWindow(IntPtr window, IntPtr state);
        internal static void CancelOwnedDialog(IntPtr owner)
        {
            EnumThreadWindows(GetCurrentThreadId(), delegate(IntPtr window, IntPtr state) {
                StringBuilder name = new StringBuilder(32);
                if (GetWindow(window, 4) == owner && GetClassName(window, name, name.Capacity) != 0 && name.ToString() == "#32770")
                    PostMessage(window, 0x111, new IntPtr(2), IntPtr.Zero);
                return true;
            }, IntPtr.Zero);
        }
        internal static uint Options(string kind)
        {
            if (kind != "directory" && kind != "model" && kind != "engine") throw new ArgumentException("Unknown picker kind.");
            return 0x40u | 0x800u | 0x8u | 0x02000000u | (kind == "directory" ? 0x20u : 0x1000u);
        }

        internal static IFileDialog Create(string kind, string initial)
        {
            IFileDialog dialog = (IFileDialog)new FileOpenDialog();
            try {
                dialog.SetOptions(Options(kind));
                dialog.SetTitle(Presentation.Language == "zh"
                    ? (kind == "directory" ? "选择文件夹" : kind == "model" ? "选择 GGUF 模型" : "选择本地引擎")
                    : (kind == "directory" ? "Select folder" : kind == "model" ? "Select GGUF model" : "Select local engine"));
                if (kind != "directory") {
                    Filter[] filters = new Filter[] { new Filter {
                        Name = kind == "model" ? "GGUF (*.gguf)" : "MoE4All (infr.exe; *.zip)",
                        Pattern = kind == "model" ? "*.gguf" : "infr.exe;*.zip" } };
                    dialog.SetFileTypes((uint)filters.Length, filters);
                    dialog.SetFileTypeIndex(1);
                }
                if (Directory.Exists(initial)) {
                    IShellItem folder;
                    Guid iid = typeof(IShellItem).GUID;
                    int hr = SHCreateItemFromParsingName(Path.GetFullPath(initial), IntPtr.Zero, ref iid, out folder);
                    if (hr >= 0) {
                        try { dialog.SetFolder(folder); } finally { Marshal.ReleaseComObject(folder); }
                    }
                }
                return dialog;
            } catch { Marshal.ReleaseComObject(dialog); throw; }
        }

        internal static string Show(IFileDialog dialog, IntPtr owner, string kind)
        {
            if (owner == IntPtr.Zero) throw new ArgumentException("The picker requires its Agent window as owner.");
            int hr = dialog.Show(owner);
            if (hr == Cancelled) return null;
            Marshal.ThrowExceptionForHR(hr);
            IShellItem selected;
            dialog.GetResult(out selected);
            try {
                IntPtr name;
                selected.GetDisplayName(0x80058000, out name);
                string path;
                try { path = Marshal.PtrToStringUni(name); } finally { Marshal.FreeCoTaskMem(name); }
                if (kind == "directory" ? Directory.Exists(path) : File.Exists(path) && PickerFileAllowed(kind, path))
                    return Path.GetFullPath(path);
                throw new IOException("The selected path is not valid for this operation.");
            } finally { Marshal.ReleaseComObject(selected); }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct Filter { [MarshalAs(UnmanagedType.LPWStr)] public string Name; [MarshalAs(UnmanagedType.LPWStr)] public string Pattern; }
        [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")] private class FileOpenDialog { }
        [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr owner);
            void SetFileTypes(uint count, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] Filter[] filters);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int alignment);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int result);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
        }
        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IShellItem
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr value);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint type, out IntPtr name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, out IShellItem item);
        [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, IntPtr state);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder value, int count);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    }

    private sealed partial class AgentWindow
    {
        private TaskCompletionSource<string> pickerCompletion;
        private NativePicker.IFileDialog activePicker;

        private string PickPath(string kind, string initial)
        {
            TaskCompletionSource<string> completion = new TaskCompletionSource<string>();
            if (closing || !IsHandleCreated) return null;
            try {
                BeginInvoke(new Action(delegate {
                    if (closing || !ready || pickerCompletion != null) { completion.TrySetResult(null); return; }
                    pickerCompletion = completion;
                    try {
                        using (NativeDialogTheme theme = new NativeDialogTheme(Handle, Presentation.Theme)) {
                            activePicker = NativePicker.Create(kind, initial);
                            completion.TrySetResult(NativePicker.Show(activePicker, Handle, kind));
                        }
                    } catch (Exception error) {
                        WriteMessage("File selection: " + error.Message, true);
                        completion.TrySetResult(null);
                    } finally {
                        if (activePicker != null) Marshal.ReleaseComObject(activePicker);
                        activePicker = null; pickerCompletion = null;
                    }
                }));
            } catch (InvalidOperationException) { return null; }
            return completion.Task.GetAwaiter().GetResult();
        }

        private void FinishPicker(string selected)
        {
            if (activePicker != null) NativePicker.CancelOwnedDialog(Handle);
            if (pickerCompletion != null) pickerCompletion.TrySetResult(selected);
        }

        // Older clients can still send chooser messages, but new selections belong to Windows.
        private void PublishPicker() { }
        private void ReceivePickerCommand(System.Collections.Generic.Dictionary<string, object> value) { }
    }
}
