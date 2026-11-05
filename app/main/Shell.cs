// The launcher window.
//
// A borderless window hosting the interface in WebView2, with its own title
// bar drawn in HTML. Because the window has no caption, this file implements
// what the caption would otherwise provide: dragging, resizing from every
// edge, double-click to maximise, snapping, and the minimise/maximise/close
// buttons the interface asks for over the bridge.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Zycord
{
    internal sealed class Shell : Form
    {
        private const int ResizeBand = 6;
        private const int MinimumW = 900;
        private const int MinimumH = 600;

        private readonly CommandLine _cli;
        private WebView2 _view;
        private NotifyIcon _tray;
        private bool _reallyClosing;
        private bool _trayHintShown;
        private System.Windows.Forms.Timer _tick;
        private readonly EdgeFilter _edges;

        public Shell(CommandLine cli)
        {
            _cli = cli;

            Text = AppInfo.Name;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(MinimumW, MinimumH);
            BackColor = Color.FromArgb(0x0C, 0x0C, 0x0E);
            ForeColor = Color.FromArgb(0xE9, 0xE9, 0xEC);
            Font = new Font("Segoe UI", 9f);
            DoubleBuffered = true;
            KeyPreview = true;

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            RestorePlacement();

            _view = new WebView2();
            _view.Dock = DockStyle.Fill;
            _view.DefaultBackgroundColor = Color.FromArgb(0x0C, 0x0C, 0x0E);
            Controls.Add(_view);

            _edges = new EdgeFilter(this);
            Application.AddMessageFilter(_edges);

            BuildTray();
            Bridge.AttachShell(this);

            Shown += OnShown;
            FormClosing += OnClosing;

            _tick = new System.Windows.Forms.Timer { Interval = 1000 };
            _tick.Tick += OnTick;
            _tick.Start();
        }

        // -------------------------------------------------------- window state

        private void RestorePlacement()
        {
            var s = Settings.Current;
            var w = Math.Max(MinimumW, s.WindowW);
            var h = Math.Max(MinimumH, s.WindowH);
            var x = s.WindowX;
            var y = s.WindowY;

            // Fall back to centring when the saved position is off-screen, which
            // happens when a monitor is disconnected between runs.
            var bounds = new Rectangle(x, y, w, h);
            if (x == 0 && y == 0 || !OnAnyScreen(bounds))
            {
                var work = Screen.PrimaryScreen.WorkingArea;
                x = work.X + (work.Width - w) / 2;
                y = work.Y + (work.Height - h) / 2;
            }

            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(x, y, w, h);
            if (s.WindowMax) WindowState = FormWindowState.Maximized;
        }

        private static bool OnAnyScreen(Rectangle bounds)
        {
            foreach (var screen in Screen.AllScreens)
            {
                if (screen.WorkingArea.IntersectsWith(bounds)) return true;
            }
            return false;
        }

        private void SaveBounds()
        {
            var s = Settings.Current;
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            s.WindowMax = WindowState == FormWindowState.Maximized;
            if (b.Width >= MinimumW && b.Height >= MinimumH)
            {
                s.WindowX = b.X;
                s.WindowY = b.Y;
                s.WindowW = b.Width;
                s.WindowH = b.Height;
            }
            Store.SaveQuiet();
        }

        // ------------------------------------------------------------- public

        public void Restore()
        {
            if (InvokeRequired) { BeginInvoke(new MethodInvoker(Restore)); return; }
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
            try { Native.SetForegroundWindow(Handle); } catch { }
        }

        public void Minimise()
        {
            if (InvokeRequired) { BeginInvoke(new MethodInvoker(Minimise)); return; }
            WindowState = FormWindowState.Minimized;
        }

        public void ToggleMaximise()
        {
            if (InvokeRequired) { BeginInvoke(new MethodInvoker(ToggleMaximise)); return; }
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
            Bridge.PushWindowState();
        }

        public void HideToTray()
        {
            if (InvokeRequired) { BeginInvoke(new MethodInvoker(HideToTray)); return; }
            Hide();
            if (_tray != null)
            {
                _tray.Visible = true;
                if (!_trayHintShown)
                {
                    _trayHintShown = true;
                    Notify("Zycord is still running", "Sessions keep going in the background. Double-click the tray icon to reopen.");
                }
            }
        }

        /// <summary>Called by the interface when it wants the window to close.</summary>
        public void RequestClose()
        {
            if (InvokeRequired) { BeginInvoke(new MethodInvoker(RequestClose)); return; }
            Close();
        }

        /// <summary>Asks the interface to switch page, for tray menu shortcuts.</summary>
        public void ShowPage(string page)
        {
            Restore();
            Bridge.Post(new Dictionary<string, object>
            {
                { "event", "navigate" },
                { "data", page }
            });
        }

        public void ApplyTheme()
        {
            try
            {
                var dark = Settings.Current.Theme == "light" ? 0 : 1;
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE,
                    ref dark, sizeof(int));
            }
            catch { }
        }

        // ---------------------------------------------------------------- tray

        private void BuildTray()
        {
            _tray = new NotifyIcon();
            try { _tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { _tray.Icon = SystemIcons.Application; }
            _tray.Text = "Zycord";
            _tray.Visible = false;

            var menu = new ContextMenuStrip();
            var open = new ToolStripMenuItem("Open Zycord");
            open.Font = new Font(menu.Font, FontStyle.Bold);
            open.Click += delegate { Restore(); };

            var sessions = new ToolStripMenuItem("Sessions");
            sessions.Click += delegate { ShowPage("sessions"); };

            var stopAll = new ToolStripMenuItem("Stop all sessions");
            stopAll.Click += delegate
            {
                var n = Sessions.StopAll();
                Bridge.PushSessions();
                Notify("Zycord", n == 0 ? "No sessions were running." : n + " session" + (n == 1 ? "" : "s") + " stopped.");
            };

            var settings = new ToolStripMenuItem("Settings");
            settings.Click += delegate { ShowPage("settings"); };

            var quit = new ToolStripMenuItem("Quit Zycord");
            quit.Click += delegate
            {
                _reallyClosing = true;
                Close();
            };

            menu.Items.Add(open);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(sessions);
            menu.Items.Add(stopAll);
            menu.Items.Add(settings);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(quit);

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { Restore(); };
        }

        private void Notify(string title, string body)
        {
            try
            {
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = body;
                _tray.Visible = true;
                _tray.ShowBalloonTip(3500);
            }
            catch { }
        }

        // -------------------------------------------------------------- events

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                var dark = 1;
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_USE_IMMERSIVE_DARK_MODE,
                    ref dark, sizeof(int));
                var round = Native.DWMWCP_ROUND;
                Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE,
                    ref round, sizeof(int));
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            // A second launch of the executable posts this message; bring the
            // existing window forward instead of opening another one.
            if (m.Msg == Native.ShowMessage && Native.ShowMessage != 0)
            {
                Restore();
                return;
            }
            base.WndProc(ref m);
        }

        private async void OnShown(object sender, EventArgs e)
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, Paths.WebView, null);
                await _view.EnsureCoreWebView2Async(env);

                var core = _view.CoreWebView2;
                var settings = core.Settings;
                settings.AreDefaultContextMenusEnabled = false;
                settings.AreDevToolsEnabled = _cli.DevTools;
                settings.IsStatusBarEnabled = false;
                settings.IsZoomControlEnabled = false;
                settings.AreBrowserAcceleratorKeysEnabled = _cli.DevTools;
                settings.IsSwipeNavigationEnabled = false;
                settings.IsGeneralAutofillEnabled = false;
                settings.IsPasswordAutosaveEnabled = false;
                settings.IsWebMessageEnabled = true;
                settings.IsBuiltInErrorPageEnabled = false;

                core.SetVirtualHostNameToFolderMapping(
                    "zycord.local", Ui.Root, CoreWebView2HostResourceAccessKind.DenyCors);

                Bridge.Attach(_view);
                core.WebMessageReceived += Bridge.OnMessage;

                core.NavigationStarting += delegate(object s, CoreWebView2NavigationStartingEventArgs a)
                {
                    // The shell only ever shows its own interface. Anything else
                    // is handed to the user's real browser instead.
                    if (!a.Uri.StartsWith("https://zycord.local", StringComparison.OrdinalIgnoreCase))
                    {
                        a.Cancel = true;
                        OpenExternal(a.Uri);
                    }
                };

                core.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs a)
                {
                    a.Handled = true;
                    OpenExternal(a.Uri);
                };

                core.ProcessFailed += delegate(object s, CoreWebView2ProcessFailedEventArgs a)
                {
                    Log.Error("webview process failed: " + a.ProcessFailedKind);
                };

                core.Navigate("https://zycord.local/index.html");
                Log.Info("shell ready, interface at " + Ui.Root + " (development=" + Ui.IsDevelopment + ")");

                if (Settings.Current.StartMinimised)
                {
                    HideToTray();
                }
            }
            catch (Exception ex)
            {
                Log.Error("webview init failed: " + ex);
                MessageBox.Show(
                    "Zycord could not start its interface.\n\n" + ex.Message +
                    "\n\nThe Microsoft Edge WebView2 Runtime is probably missing or damaged. " +
                    "Install the Evergreen Runtime from Microsoft, then run Zycord again.",
                    "Zycord", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _reallyClosing = true;
                Close();
            }
        }

        private void OnTick(object sender, EventArgs e)
        {
            var stopped = Sessions.Tick();
            if (stopped != null && stopped.Count > 0)
            {
                Bridge.PushSessions();
                Bridge.PushEvent("autostop", stopped);
            }
        }

        private static void OpenExternal(string uri)
        {
            try
            {
                if (string.IsNullOrEmpty(uri)) return;
                if (uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri)
                    {
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex) { Log.Warn("open external failed: " + ex.Message); }
        }

        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!_reallyClosing &&
                e.CloseReason == CloseReason.UserClosing &&
                Settings.Current.MinimiseToTray)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }

            _reallyClosing = true;
            SaveBounds();

            try { _tick.Stop(); } catch { }
            try { Application.RemoveMessageFilter(_edges); } catch { }

            var running = Sessions.Snapshot().Count;
            Sessions.StopAll();
            if (running > 0) Log.Info("closed with " + running + " session(s) stopped");

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
            }
            if (_view != null) _view.Dispose();
        }

        // -------------------------------------------------------------- resizing

        /// <summary>
        /// Gives a borderless window the resize handles its caption would have
        /// provided. The WebView2 control is a child window and answers its own
        /// hit tests, so this has to run as a message filter rather than an
        /// override on the form.
        /// </summary>
        private sealed class EdgeFilter : IMessageFilter
        {
            private const int WM_NCHITTEST = 0x0084;
            private const int HTCLIENT = 1;
            private const int HTLEFT = 10;
            private const int HTRIGHT = 11;
            private const int HTTOP = 12;
            private const int HTTOPLEFT = 13;
            private const int HTTOPRIGHT = 14;
            private const int HTBOTTOM = 15;
            private const int HTBOTTOMLEFT = 16;
            private const int HTBOTTOMRIGHT = 17;

            private readonly Shell _shell;

            public EdgeFilter(Shell shell) { _shell = shell; }

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WM_NCHITTEST) return false;
                if (_shell.WindowState == FormWindowState.Maximized) return false;
                if (_shell.IsDisposed) return false;

                var x = unchecked((short)(long)m.LParam);
                var y = unchecked((short)((long)m.LParam >> 16));
                var p = _shell.PointToClient(new Point(x, y));
                var size = _shell.ClientSize;

                var left = p.X <= ResizeBand;
                var right = p.X >= size.Width - ResizeBand;
                var top = p.Y <= ResizeBand;
                var bottom = p.Y >= size.Height - ResizeBand;

                int code;
                if (top && left) code = HTTOPLEFT;
                else if (top && right) code = HTTOPRIGHT;
                else if (bottom && left) code = HTBOTTOMLEFT;
                else if (bottom && right) code = HTBOTTOMRIGHT;
                else if (left) code = HTLEFT;
                else if (right) code = HTRIGHT;
                else if (top) code = HTTOP;
                else if (bottom) code = HTBOTTOM;
                else return false;

                m.Result = new IntPtr(code);
                return true;
            }
        }
    }

}
