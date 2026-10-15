using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal static partial class MoE4All
{
    internal static bool PickerFileAllowed(string kind, string file)
    {
        return kind == "model" ? Path.GetExtension(file).Equals(".gguf", StringComparison.OrdinalIgnoreCase)
            : kind == "engine" && (Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals("infr.exe", StringComparison.OrdinalIgnoreCase));
    }

    private sealed partial class AgentWindow
    {
        private TaskCompletionSource<string> pickerCompletion;
        private string pickerId, pickerKind;
        private int pickerRevision;
        private object pickerState;

        private string PickPath(string kind, string initial)
        {
            TaskCompletionSource<string> completion = new TaskCompletionSource<string>();
            if (closing || !IsHandleCreated) return null;
            BeginInvoke(new Action(delegate {
                if (closing || !ready || pickerCompletion != null) { completion.SetResult(null); return; }
                pickerCompletion = completion; pickerId = Guid.NewGuid().ToString("N"); pickerKind = kind;
                LoadPicker(initial);
            }));
            return completion.Task.GetAwaiter().GetResult();
        }

        private void PublishPicker()
        {
            if (!closing && ready && desktopBridgeReady && webView.CoreWebView2 != null)
                webView.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new { type = "moe4all-picker-state", picker = pickerState }));
        }

        private void FinishPicker(string selected)
        {
            TaskCompletionSource<string> completion = pickerCompletion;
            pickerCompletion = null; pickerState = null; pickerId = null; pickerRevision++;
            PublishPicker();
            if (completion != null) completion.TrySetResult(selected);
        }

        private void LoadPicker(string requested, string folderName = "")
        {
            string id = pickerId, kind = pickerKind;
            int revision = ++pickerRevision;
            pickerState = new { id = id, kind = kind, path = requested, loading = true, error = "", entries = new object[0], roots = new string[0], parent = "", truncated = false };
            PublishPicker();
            Task.Run(delegate {
                object result;
                try {
                    if (!Path.IsPathRooted(requested) || (requested.Length >= 2 && requested[1] == ':' && (requested.Length < 3 || requested[2] != '\\')))
                        throw new IOException("Use an absolute directory path.");
                    string directory = Path.GetFullPath(requested);
                    if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
                    if (folderName.Length > 0) {
                        if (folderName != folderName.Trim() || folderName == "." || folderName == ".." || folderName.EndsWith(".")
                            || folderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(folderName) != folderName)
                            throw new IOException("Enter a single folder name.");
                        Directory.CreateDirectory(Path.Combine(directory, folderName));
                    }
                    List<object> entries = new List<object>();
                    bool truncated = false;
                    int scanned = 0;
                    foreach (string entry in Directory.EnumerateFileSystemEntries(directory)) {
                        if (pickerRevision != revision) return;
                        if (entries.Count >= 1000 || ++scanned > 10000) { truncated = true; break; }
                        bool folder;
                        try { folder = (File.GetAttributes(entry) & FileAttributes.Directory) != 0; }
                        catch (IOException) { continue; }
                        catch (UnauthorizedAccessException) { continue; }
                        if (folder || PickerFileAllowed(kind, entry)) entries.Add(new { name = Path.GetFileName(entry), path = entry, directory = folder });
                    }
                    DirectoryInfo parent = Directory.GetParent(directory);
                    result = new { id = id, kind = kind, path = directory, loading = false, error = "", entries = entries,
                        roots = Directory.GetLogicalDrives(), parent = parent == null ? "" : parent.FullName, truncated = truncated };
                } catch (Exception error) {
                    result = new { id = id, kind = kind, path = requested, loading = false, error = error.Message,
                        entries = new object[0], roots = Directory.GetLogicalDrives(), parent = "", truncated = false };
                }
                if (!closing && IsHandleCreated) BeginInvoke(new Action(delegate {
                    if (pickerId != id || pickerRevision != revision) return;
                    pickerState = result; PublishPicker();
                }));
            });
        }

        private void ReceivePickerCommand(Dictionary<string, object> value)
        {
            if (pickerCompletion == null || CapabilityField(value, "id") != pickerId) return;
            string command = CapabilityField(value, "command"), path = CapabilityField(value, "path");
            if (command == "cancel") { FinishPicker(null); return; }
            if (command == "list") { LoadPicker(path); return; }
            if (command == "create") { LoadPicker(path, CapabilityField(value, "name")); return; }
            if (command != "select") return;
            try {
                if (!Path.IsPathRooted(path)) throw new IOException("Use an absolute path.");
                path = Path.GetFullPath(path);
                if (pickerKind == "directory" ? Directory.Exists(path) : File.Exists(path) && PickerFileAllowed(pickerKind, path)) FinishPicker(path);
                else LoadPicker(Directory.Exists(path) ? path : Path.GetDirectoryName(path));
            } catch (Exception error) { WriteMessage("File selection: " + error.Message, true); }
        }
    }
}
