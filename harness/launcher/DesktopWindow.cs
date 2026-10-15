using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static partial class MoE4All
{
    private static int RunDesktopWindowTest(string root)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Rectangle work = new Rectangle(-1920, 0, 1920, 1080);
        Rectangle clamped = ClampDesktopBounds(new Rectangle(-5000, 5000, 4000, 2000), work);
        if (!work.Contains(clamped) || clamped.Width != 1896 || clamped.Height != 1056)
            throw new InvalidOperationException("Saved bounds must stay within the current working area.");
        Rectangle compact = new Rectangle(24, 24, 976, 672);
        if (ClampDesktopBounds(compact, new Rectangle(0, 0, 1024, 720)) != compact)
            throw new InvalidOperationException("A compact screen must restore its saved dimensions exactly.");
        string fixture = Path.Combine(root, "window-state-fixture");
        using (AgentWindow first = new AgentWindow(fixture, false)) first.ValidateDesktopWindowForTest(false);
        using (AgentWindow next = new AgentWindow(fixture, false)) next.ValidateDesktopWindowForTest(true);
        Console.WriteLine("Window state passed: normal bounds round-trip, maximized state, minimized preservation, off-screen clamping.");
        return 0;
    }

    internal static Rectangle ClampDesktopBounds(Rectangle saved, Rectangle work)
    {
        int minimumWidth = Math.Min(980, Math.Max(320, work.Width - 48));
        int minimumHeight = Math.Min(680, Math.Max(300, work.Height - 48));
        int width = Math.Min(Math.Max(minimumWidth, saved.Width), Math.Max(320, work.Width - 24));
        int height = Math.Min(Math.Max(minimumHeight, saved.Height), Math.Max(300, work.Height - 24));
        int left = Math.Max(work.Left, Math.Min(saved.Left, work.Right - width));
        int top = Math.Max(work.Top, Math.Min(saved.Top, work.Bottom - height));
        return new Rectangle(left, top, width, height);
    }

    private sealed partial class AgentWindow
    {
        private bool desktopWindowLoaded;
        private FormWindowState trackedDesktopState = FormWindowState.Normal;

        public void ValidateDesktopWindowForTest(bool returning)
        {
            desktopWindowLoaded = true;
            string file = Path.Combine(root, "data/desktop-window.json");
            if (returning) {
                RestoreDesktopWindow();
                Rectangle expected = ClampDesktopBounds(new Rectangle(100, 100, 1100, 720), Screen.FromRectangle(new Rectangle(100, 100, 1100, 720)).WorkingArea);
                if (Bounds != expected) throw new InvalidOperationException("The saved window size was not restored.");
                Dictionary<string, object> saved = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(file));
                saved["maximized"] = true;
                File.WriteAllText(file, new JavaScriptSerializer().Serialize(saved));
                RestoreDesktopWindow();
                if (WindowState != FormWindowState.Maximized) throw new InvalidOperationException("The maximized state was not restored.");
            } else {
                Bounds = new Rectangle(100, 100, 1100, 720);
                SaveDesktopWindow();
                string normal = File.ReadAllText(file);
                WindowState = FormWindowState.Minimized;
                SaveDesktopWindow();
                if (File.ReadAllText(file) != normal) throw new InvalidOperationException("Minimizing overwrote the saved window size.");
            }
        }

        private void TrackDesktopWindowState()
        {
            if (WindowState == trackedDesktopState) return;
            trackedDesktopState = WindowState;
            SaveDesktopWindow();
        }

        private void RestoreDesktopWindow()
        {
            try {
                string file = Path.Combine(root, "data/desktop-window.json");
                if (!File.Exists(file)) return;
                Dictionary<string, object> data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(file, Encoding.UTF8));
                if (Convert.ToInt32(data["schema"]) != 1) return;
                Rectangle saved = new Rectangle(Convert.ToInt32(data["x"]), Convert.ToInt32(data["y"]), Convert.ToInt32(data["width"]), Convert.ToInt32(data["height"]));
                Rectangle work = Screen.FromRectangle(saved).WorkingArea;
                StartPosition = FormStartPosition.Manual;
                MinimumSize = new Size(Math.Min(980, Math.Max(320, work.Width - 48)), Math.Min(680, Math.Max(300, work.Height - 48)));
                Bounds = ClampDesktopBounds(saved, work);
                if (data.ContainsKey("maximized") && data["maximized"] is bool && (bool)data["maximized"]) WindowState = FormWindowState.Maximized;
            } catch (Exception error) { WriteMessage("Window position: " + error.Message); }
        }

        private void SaveDesktopWindow()
        {
            if (!desktopWindowLoaded || WindowState == FormWindowState.Minimized) return;
            try {
                Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                string directory = Path.Combine(root, "data"); Directory.CreateDirectory(directory);
                string json = new JavaScriptSerializer().Serialize(new { schema = 1, x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height,
                    maximized = WindowState == FormWindowState.Maximized });
                string file = Path.Combine(directory, "desktop-window.json"), temporary = file + ".tmp";
                File.WriteAllText(temporary, json, new UTF8Encoding(false));
                if (File.Exists(file)) File.Replace(temporary, file, null); else File.Move(temporary, file);
            } catch (Exception error) { WriteMessage("Saving window position: " + error.Message); }
        }
    }
}
