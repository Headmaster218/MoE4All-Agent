using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static partial class MoE4All
{
    internal static Rectangle CaptionExclusion(Size client, Rectangle caption, int buttonWidth)
    {
        int left = Math.Max(0, caption.Left - buttonWidth);
        return Rectangle.Intersect(new Rectangle(Point.Empty, client), new Rectangle(left, 0, client.Width - left, Math.Max(1, caption.Bottom)));
    }

    private sealed partial class AgentWindow
    {
        private bool integratedChrome;
        private int lastCaptionWidth;
        [StructLayout(LayoutKind.Sequential)] private struct FrameRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct FrameMargins { public int Left, Right, Top, Bottom; }
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref FrameMargins margins);
        [DllImport("dwmapi.dll")] private static extern bool DwmDefWindowProc(IntPtr window, int message, IntPtr wParam, IntPtr lParam, out IntPtr result);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out FrameRect value, int size);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

        private int ResizeBorder()
        {
            try { return Math.Max(1, GetSystemMetricsForDpi(32, (uint)DeviceDpi) + GetSystemMetricsForDpi(92, (uint)DeviceDpi)); }
            catch (EntryPointNotFoundException) { return Math.Max(1, SystemInformation.FrameBorderSize.Width); }
        }

        private void EnableDesktopChrome()
        {
            integratedChrome = true;
            SizeChanged += delegate { LayoutDesktopChrome(); };
            webView.ZoomFactorChanged += delegate { LayoutDesktopChrome(); };
            HandleCreated += delegate { BeginInvoke(new Action(LayoutDesktopChrome)); };
            WireStartupDrag(startup);
        }

        private void WireStartupDrag(Control surface)
        {
            surface.MouseDown += delegate(object sender, MouseEventArgs e) {
                if (!integratedChrome || !startup.Visible || e.Button != MouseButtons.Left) return;
                Point point = ((Control)sender).PointToScreen(e.Location);
                Point local = PointToClient(point);
                if (local.Y < 6 * DeviceDpi / 96 || local.Y >= 36 * DeviceDpi / 96 || local.X >= browserButton.Left) return;
                ReleaseCapture();
                SendMessage(Handle, 0xa1, new IntPtr(2), new IntPtr((point.Y << 16) | (point.X & 0xffff)));
            };
            foreach (Control child in surface.Controls) WireStartupDrag(child);
        }

        [DllImport("user32.dll")] private static extern bool ReleaseCapture();

        protected override void WndProc(ref Message message)
        {
            if (integratedChrome && message.Msg == 0x83 && message.WParam != IntPtr.Zero) {
                if (IsZoomed(Handle)) {
                    FrameRect client = (FrameRect)Marshal.PtrToStructure(message.LParam, typeof(FrameRect));
                    int border = ResizeBorder();
                    client.Left += border; client.Top += border; client.Right -= border; client.Bottom -= border;
                    Marshal.StructureToPtr(client, message.LParam, false);
                }
                message.Result = IntPtr.Zero;
                return;
            }
            IntPtr result;
            if (integratedChrome && message.Msg == 0x84 && DwmDefWindowProc(Handle, message.Msg, message.WParam, message.LParam, out result)) {
                message.Result = result;
                return;
            }
            base.WndProc(ref message);
            if (integratedChrome && message.Msg == 0x84 && message.Result.ToInt32() == 1) {
                long raw = message.LParam.ToInt64();
                Point point = PointToClient(new Point((short)(raw & 0xffff), (short)((raw >> 16) & 0xffff)));
                Rectangle caption = CaptionButtons();
                if (caption.Contains(point)) {
                    int segment = Math.Min(2, Math.Max(0, (point.X - caption.Left) * 3 / caption.Width));
                    message.Result = new IntPtr(segment == 0 ? 8 : segment == 1 ? 9 : 20);
                }
            }
            if (integratedChrome && message.Msg == 0x84 && message.Result.ToInt32() == 1 && WindowState == FormWindowState.Normal) {
                long position = message.LParam.ToInt64();
                Point point = PointToClient(new Point((short)(position & 0xffff), (short)((position >> 16) & 0xffff)));
                int edge = Math.Max(4, 6 * DeviceDpi / 96);
                bool left = point.X < edge, right = point.X >= ClientSize.Width - edge;
                bool top = point.Y < edge, bottom = point.Y >= ClientSize.Height - edge;
                message.Result = new IntPtr(top ? (left ? 13 : right ? 14 : 12) : bottom ? (left ? 16 : right ? 17 : 15) : left ? 10 : right ? 11 : 1);
            }
            if (integratedChrome && (message.Msg == 0x2e0 || message.Msg == 0x31a || message.Msg == 0x320)) LayoutDesktopChrome();
        }

        private Rectangle CaptionButtons()
        {
            FrameRect native;
            if (DwmGetWindowAttribute(Handle, 5, out native, Marshal.SizeOf(typeof(FrameRect))) == 0 && native.Right > native.Left) {
                Point origin = PointToClient(new Point(Left + native.Left, Top + native.Top));
                return new Rectangle(origin, new Size(native.Right - native.Left, native.Bottom - native.Top));
            }
            int scale = Math.Max(1, DeviceDpi);
            return new Rectangle(ClientSize.Width - 138 * scale / 96, 0, 138 * scale / 96, 30 * scale / 96);
        }

        private void LayoutDesktopChrome()
        {
            if (!integratedChrome || !IsHandleCreated || browserButton == null || webView == null || closing) return;
            Rectangle caption = CaptionButtons();
            int width = Math.Max(30, 34 * DeviceDpi / 96);
            Rectangle exclusion = CaptionExclusion(ClientSize, caption, width);
            FrameMargins margins = new FrameMargins { Top = Math.Max(1, exclusion.Height) };
            DwmExtendFrameIntoClientArea(Handle, ref margins);
            browserButton.Dock = DockStyle.None;
            browserButton.Bounds = new Rectangle(exclusion.Left, 0, Math.Min(width, exclusion.Width), exclusion.Height);
            browserButton.BringToFront();
            Region region = new Region(new Rectangle(Point.Empty, webView.ClientSize));
            region.Exclude(exclusion);
            if (WindowState == FormWindowState.Normal) {
                int edge = Math.Max(4, 6 * DeviceDpi / 96);
                region.Exclude(new Rectangle(0, 0, ClientSize.Width, edge));
                region.Exclude(new Rectangle(0, ClientSize.Height - edge, ClientSize.Width, edge));
                region.Exclude(new Rectangle(0, 0, edge, ClientSize.Height));
                region.Exclude(new Rectangle(ClientSize.Width - edge, 0, edge, ClientSize.Height));
            }
            Region old = webView.Region;
            webView.Region = region;
            if (old != null) old.Dispose();
            int inset = (int)Math.Ceiling(exclusion.Width * 96.0 / (Math.Max(1, DeviceDpi) * Math.Max(.25, webView.ZoomFactor)));
            if (lastCaptionWidth != inset) {
                lastCaptionWidth = inset;
                if (webView.CoreWebView2 != null) webView.CoreWebView2.ExecuteScriptAsync(ChromeScript(inset));
            }
        }

        internal static string ChromeScript(int inset)
        {
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("MoE4All.DesktopChrome.js"))
            using (StreamReader reader = new StreamReader(resource))
                return reader.ReadToEnd().Replace("__MOE4ALL_CAPTION_WIDTH__", Math.Max(0, inset).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private async System.Threading.Tasks.Task InitializeDesktopChrome()
        {
            // The installed WebView2 exposes native drag regions, including double-click and system menu.
            try { webView.CoreWebView2.Settings.IsNonClientRegionSupportEnabled = true; }
            catch (NotImplementedException) {
                integratedChrome = false;
                Region old = webView.Region; webView.Region = null;
                if (old != null) old.Dispose();
                SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x37);
                browserButton.SetBounds(Math.Max(0, ClientSize.Width - 34), 0, 34, 30);
                lastCaptionWidth = 34;
                WriteMessage("The installed WebView2 does not support integrated window chrome; retaining the standard Windows title bar.");
            }
            await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ChromeScript(lastCaptionWidth));
            LayoutDesktopChrome();
        }

        public void ValidateDesktopChromeForTest()
        {
            Show(); Application.DoEvents();
            WindowState = FormWindowState.Normal;
            Bounds = ClampDesktopBounds(new Rectangle(100, 100, 1380, 920), Screen.FromControl(this).WorkingArea);
            Application.DoEvents();
            webView.Visible = true; startup.Visible = false; PerformLayout(); LayoutDesktopChrome();
            Rectangle caption = CaptionButtons();
            Console.WriteLine("Native chrome: window=" + Bounds + ", client=" + ClientRectangle + ", origin=" + PointToScreen(Point.Empty) + ", caption=" + caption + ", browser=" + browserButton.Bounds);
            if (!integratedChrome || browserButton.Parent != this || browserButton.Top != 0 || browserButton.Bottom > 40 * DeviceDpi / 96)
                throw new InvalidOperationException("Browser action must share the native caption band.");
            if (browserButton.Right > caption.Left + 1 || browserButton.Left < 0)
                throw new InvalidOperationException("Browser action overlaps native caption buttons.");
            Point client = PointToScreen(Point.Empty);
            if (client.Y - Top >= SystemInformation.CaptionHeight)
                throw new InvalidOperationException("Native title band was not reclaimed.");
            if (webView.Top != 0 || webView.Height != ClientSize.Height)
                throw new InvalidOperationException("Web content must use the full client height.");
            int y = Math.Max(1, browserButton.Height / 2);
            if (webView.Region.IsVisible(ClientSize.Width - 20, y))
                throw new InvalidOperationException("Web content must not cover native window controls.");
            foreach (int offset in new int[] { 23, 69, 115 }) {
                Point point = PointToScreen(new Point(caption.Right - offset, y));
                int hit = SendMessage(Handle, 0x84, IntPtr.Zero, new IntPtr((point.Y << 16) | (point.X & 0xffff))).ToInt32();
                int expected = offset == 23 ? 20 : offset == 69 ? 9 : 8;
                if (hit != expected) throw new InvalidOperationException("Native caption hit test failed: " + hit + " vs " + expected);
            }
            Rectangle restored = Bounds;
            WindowState = FormWindowState.Maximized;
            Application.DoEvents(); LayoutDesktopChrome();
            Rectangle work = Screen.FromControl(this).WorkingArea;
            Point origin = PointToScreen(Point.Empty);
            Console.WriteLine("Maximized: bounds=" + Bounds + ", origin=" + origin + ", client=" + ClientSize + ", work=" + work);
            if (origin != work.Location || ClientSize != work.Size)
                throw new InvalidOperationException("Maximized content extends outside the working area.");
            WindowState = FormWindowState.Normal; Bounds = restored;
            Application.DoEvents(); LayoutDesktopChrome();
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "chrome-script.js"), ChromeScript(lastCaptionWidth));
            Application.DoEvents(); System.Threading.Thread.Sleep(300);
            using (Bitmap capture = new Bitmap(Width, Height)) {
                using (Graphics graphics = Graphics.FromImage(capture)) graphics.CopyFromScreen(Left, Top, 0, 0, capture.Size);
                capture.Save(System.IO.Path.Combine(root, "native-chrome.png"));
            }
            Close();
        }
    }

    internal static void ValidateNativeChrome(string root)
    {
        foreach (int dpi in new int[] { 96, 144, 192 }) {
            Rectangle caption = new Rectangle(1200 - 138 * dpi / 96, 0, 138 * dpi / 96, 30 * dpi / 96);
            Rectangle excluded = CaptionExclusion(new Size(1200, 800), caption, 34 * dpi / 96);
            if (excluded.Right != 1200 || excluded.Left >= caption.Left || excluded.Bottom != caption.Bottom)
                throw new InvalidOperationException("Caption geometry failed at DPI " + dpi);
        }
        using (AgentWindow window = new AgentWindow(root, false)) window.ValidateDesktopChromeForTest();
        Console.WriteLine("Chrome passed: full-height content, integrated browser action, native caption exclusion, 100/150/200% DPI geometry.");
    }
}
