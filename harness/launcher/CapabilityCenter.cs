using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static partial class MoE4All
{
    internal static string FindBundledNode(string installRoot)
    {
        foreach (string relative in new[] { "node.exe", "harness/bootstrap/node.exe" })
        {
            string node = Path.Combine(installRoot, relative);
            if (File.Exists(node)) return Path.GetFullPath(node);
        }
        string runtime = Path.Combine(installRoot, "harness/runtime");
        if (Directory.Exists(runtime))
        {
            foreach (string directory in Directory.GetDirectories(runtime, "node-v*-win-x64"))
            {
                string node = Path.Combine(directory, "node.exe");
                if (File.Exists(node)) return Path.GetFullPath(node);
            }
        }
        throw new FileNotFoundException("The bundled Node runtime is missing.");
    }

    private sealed partial class AgentWindow : Form
    {
        private Dictionary<string, object> capabilityState;
        private volatile bool capabilityBusy;
        private volatile bool capabilityProfileRepairRequired = true;
        private IntPtr capabilityJob;
        private bool capabilityHealthRequested, capabilityHealthConfirmed, capabilityStarted;
        private DateTime nextCapabilityCheck = DateTime.UtcNow.AddHours(6);
        private readonly System.Windows.Forms.Timer capabilityPoll = new System.Windows.Forms.Timer { Interval = 1500 };

        private static string CapabilityField(Dictionary<string, object> value, string key)
        {
            object field;
            return value != null && value.TryGetValue(key, out field) && field != null ? Convert.ToString(field, System.Globalization.CultureInfo.InvariantCulture) : "";
        }
        private static bool CapabilityFlag(Dictionary<string, object> value, string key) { return CapabilityField(value, key) == "True"; }
        private static Dictionary<string, object> CapabilityObject(Dictionary<string, object> value, string key)
        {
            object field;
            return value != null && value.TryGetValue(key, out field) ? field as Dictionary<string, object> : null;
        }

        private void RefreshCapabilities()
        {
            if (closing || !ready || !desktopBridgeReady || webView.CoreWebView2 == null || capabilityState == null) return;
            webView.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 }.Serialize(new {
                type = "moe4all-capabilities-state", snapshot = capabilityState, busy = capabilityBusy
            }));
        }

        private int RunCapabilities(string arguments, bool stream)
        {
            if (arguments == "apply") capabilityProfileRepairRequired = true;
            string cli = Path.Combine(root, "harness/capabilities/cli.cjs");
            if (!File.Exists(cli)) throw new FileNotFoundException("The capability center is missing. Reinstall the complete Agent package.");
            ProcessStartInfo start = new ProcessStartInfo(FindBundledNode(root));
            start.Arguments = Quote(cli) + " " + arguments + " --root " + Quote(root);
            start.WorkingDirectory = root;
            start.UseShellExecute = false; start.CreateNoWindow = true;
            start.RedirectStandardOutput = true; start.RedirectStandardError = true;
            start.StandardOutputEncoding = new UTF8Encoding(false); start.StandardErrorEncoding = new UTF8Encoding(false);
            string temporary = Path.Combine(root, "cache/temp"); Directory.CreateDirectory(temporary);
            start.EnvironmentVariables["TEMP"] = temporary; start.EnvironmentVariables["TMP"] = temporary; start.EnvironmentVariables["TMPDIR"] = temporary;
            start.EnvironmentVariables["npm_config_cache"] = Path.Combine(root, "cache/node/npm");
            IntPtr ownedJob = CreateChildJob();
            lock (this) capabilityJob = ownedJob;
            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = start;
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        const string prefix = "@@MOE4ALL_CAPABILITIES\t";
                        if (!e.Data.StartsWith(prefix, StringComparison.Ordinal)) { WriteMessage(e.Data); return; }
                        try
                        {
                            Dictionary<string, object> snapshot = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 }
                                .Deserialize<Dictionary<string, object>>(e.Data.Substring(prefix.Length));
                            object repair;
                            if (arguments == "apply" && snapshot.TryGetValue("profileRepairRequired", out repair) && repair is bool)
                                capabilityProfileRepairRequired = (bool)repair;
                            if (IsHandleCreated && !closing) BeginInvoke(new Action(delegate { capabilityState = snapshot; RefreshCapabilities(); }));
                        }
                        catch (Exception error) { WriteMessage("Capability status: " + error.Message, true); }
                    };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                    {
                        if (e.Data == null) return;
                        WriteMessage(e.Data, true);
                        if (stream && IsHandleCreated && !closing) BeginInvoke(new Action(delegate { CapabilityError(e.Data); }));
                    };
                    if (!process.Start()) throw new InvalidOperationException("Cannot start the capability center.");
                    if (!AssignProcessToJobObject(ownedJob, process.Handle))
                    {
                        process.Kill();
                        throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                    }
                    process.BeginOutputReadLine(); process.BeginErrorReadLine(); process.WaitForExit();
                    return process.ExitCode;
                }
            }
            finally { lock (this) { if (capabilityJob == ownedJob) { capabilityJob = IntPtr.Zero; CloseHandle(ownedJob); } } }
        }

        private void CapabilityError(string message)
        {
            if (!closing && ready && desktopBridgeReady && webView.CoreWebView2 != null)
                webView.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new { type = "moe4all-capabilities-error", message = message }));
        }

        private void BeginCapabilityAction(string command, bool automatic)
        {
            if (closing || capabilityBusy || updateInstalling)
            {
                if (!automatic && !closing) CapabilityError(Presentation.Language == "zh" ? "另一项更新操作正在进行，请稍后重试。" : "Another update operation is running. Try again shortly.");
                return;
            }
            capabilityBusy = true; RefreshCapabilities();
            Thread worker = new Thread(delegate()
            {
                int result = -1;
                try { result = RunCapabilities(command, !automatic); }
                catch (Exception error)
                {
                    WriteMessage(error.ToString(), true);
                    if (!automatic && !closing) BeginInvoke(new Action(delegate { CapabilityError(error.Message); }));
                }
                if (closing) return;
                BeginInvoke(new Action(delegate
                {
                    capabilityBusy = false;
                    if (command == "healthy" && result == 0) capabilityHealthConfirmed = true;
                    RefreshCapabilities();
                    if (capabilityHealthRequested && !capabilityHealthConfirmed && command != "healthy") ConfirmCapabilityHealth();
                    else if (command == "healthy" && result == 0 && !capabilityStarted)
                    {
                        capabilityStarted = true;
                        BeginCapabilityAction("engine-bootstrap", true);
                    }
                    else if (command == "engine-bootstrap")
                    {
                        if (CapabilityFlag(CapabilityObject(capabilityState, "settings"), "autoCheck"))
                        {
                            StartUpdateAction("Check", false);
                            BeginCapabilityAction("check --automatic", true);
                        }
                    }
                    else if (command.StartsWith("settings ", StringComparison.Ordinal) && result == 0)
                    {
                        StartUpdateAction("Check", false);
                        BeginCapabilityAction("check --force", true);
                    }
                }));
            });
            worker.IsBackground = true; worker.Name = "MoE4All capability center"; worker.Start();
        }

        private void StartCapabilityChecks()
        {
            capabilityPoll.Tick += delegate
            {
                string phase = CapabilityField(CapabilityObject(capabilityState, "engine"), "phase");
                if (!closing && ready && !capabilityBusy && !updateInstalling && Array.IndexOf(new[] { "checking", "downloading", "extracting", "verifying", "finalizing" }, phase) >= 0)
                    BeginCapabilityAction("engine-status", true);
                else if (!closing && ready && capabilityHealthConfirmed && !capabilityBusy && !updateBusy && DateTime.UtcNow >= nextCapabilityCheck
                    && CapabilityFlag(CapabilityObject(capabilityState, "settings"), "autoCheck"))
                {
                    nextCapabilityCheck = DateTime.UtcNow.AddHours(6);
                    StartUpdateAction("Check", false); BeginCapabilityAction("check --automatic", true);
                }
            };
            capabilityPoll.Start(); BeginCapabilityAction("status", true);
        }
        private void ConfirmCapabilityHealth()
        {
            capabilityHealthRequested = true;
            if (!capabilityBusy && !capabilityHealthConfirmed) BeginCapabilityAction("healthy", true);
        }
        private bool ConfirmAgentInstall()
        {
            if (!capabilityBusy && !CapabilityFlag(capabilityState, "pending")) return true;
            CapabilityError(Presentation.Language == "zh" ? "请先完成或取消待处理的插件变更。" : "Finish or cancel the pending plugin change first.");
            return false;
        }
        private void RestartForCapabilities()
        {
            if (capabilityBusy || updateBusy || !CapabilityFlag(capabilityState, "pending")) return;
            ProcessStartInfo start = PowerShellStart(root);
            string acknowledgement = Path.Combine(root, "cache", "restart-" + Guid.NewGuid().ToString("N") + ".ready");
            start.Arguments = "-NoLogo -NoProfile -NonInteractive -File " + Quote(Path.Combine(root, "harness/scripts/Restart-Agent.ps1"))
                + " -InstallRoot " + Quote(root) + " -ParentPid " + Process.GetCurrentProcess().Id + " -ReadyFile " + Quote(acknowledgement);
            using (Process helper = Process.Start(start))
            {
                for (int attempt = 0; attempt < 500 && !File.Exists(acknowledgement) && !helper.HasExited; attempt++) Thread.Sleep(10);
                if (!File.Exists(acknowledgement)) { CapabilityError(Presentation.Language == "zh" ? "重启助手未就绪，当前 Agent 保持运行。" : "The restart helper is not ready. The current Agent is still running."); return; }
            }
            File.Delete(acknowledgement); Close();
        }
        private void CancelCapabilities()
        {
            if (updateInstalling) return;
            lock (this) { IntPtr current = capabilityJob; capabilityJob = IntPtr.Zero; if (current != IntPtr.Zero) CloseHandle(current); }
            if (!capabilityBusy) { BeginCapabilityAction("cancel", false); return; }
            System.Windows.Forms.Timer wait = new System.Windows.Forms.Timer { Interval = 100 };
            wait.Tick += delegate { if (!capabilityBusy || closing) { wait.Stop(); wait.Dispose(); if (!closing) BeginCapabilityAction("cancel", false); } };
            wait.Start();
        }
        private void CloseCapabilities()
        {
            capabilityPoll.Dispose();
            lock (this) { IntPtr current = capabilityJob; capabilityJob = IntPtr.Zero; if (current != IntPtr.Zero) CloseHandle(current); }
        }

        private void ReceiveCapabilityCommand(Dictionary<string, object> value)
        {
            string command = CapabilityField(value, "command"), id = CapabilityField(value, "id"), version = CapabilityField(value, "version");
            bool confirmed = value.ContainsKey("confirmed") && value["confirmed"] is bool && (bool)value["confirmed"];
            if (command == "status") { RefreshCapabilities(); if (capabilityState == null) BeginCapabilityAction("status", true); }
            if (command == "check") { StartUpdateAction("Check", false); BeginCapabilityAction("check --force", false); }
            if (command == "prepare" && confirmed && System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9-]{1,80}$")
                && System.Text.RegularExpressions.Regex.IsMatch(version, "^[0-9A-Za-z.+-]{1,100}$"))
                BeginCapabilityAction("prepare --confirm --id " + Quote(id) + " --version " + Quote(version), false);
            if (command == "engine-install" && confirmed && System.Text.RegularExpressions.Regex.IsMatch(version, "^[0-9A-Za-z.+-]{1,100}$"))
                BeginCapabilityAction("engine-install --confirm --version " + Quote(version), false);
            if (command == "engine-cancel") BeginCapabilityAction("engine-cancel", false);
            if (command == "rollback" && confirmed) BeginCapabilityAction("rollback --confirm", false);
            if (command == "restart" && confirmed) RestartForCapabilities();
            if (command == "cancel") CancelCapabilities();
            if (command == "settings" && value.ContainsKey("autoCheck") && value["autoCheck"] is bool && value.ContainsKey("preview") && value["preview"] is bool)
                BeginCapabilityAction("settings --autoCheck " + CapabilityFlag(value, "autoCheck").ToString().ToLowerInvariant() + " --preview " + CapabilityFlag(value, "preview").ToString().ToLowerInvariant(), false);
            if (command == "skip" && System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9-]{1,80}$")
                && System.Text.RegularExpressions.Regex.IsMatch(version, "^[0-9A-Za-z.+-]{1,100}$"))
                BeginCapabilityAction("skip --id " + Quote(id) + " --version " + Quote(version), false);
        }
    }
}
