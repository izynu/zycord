// Zycord session stub.
//
// A minimal process that Discord recognises as a running game. It holds its
// process name, shows how long the session has run, and exits when the user
// ends it or when the launcher that started it goes away.
//
// It has no game code, no network code and no access to Discord. Everything
// it does is visible in this file.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Zycord
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] argv)
        {
            try { Native.SetProcessDPIAware(); } catch { }

            var opts = Options.Parse(argv);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (opts.Hidden)
            {
                // No window at all. The process still counts as running, and it
                // still follows the launcher's lifetime.
                if (opts.ParentPid > 0) WatchParent(opts.ParentPid, null);
                Application.Run(new ApplicationContext());
                return;
            }

            var form = new SessionForm(opts);
            if (opts.ParentPid > 0) WatchParent(opts.ParentPid, form);
            Application.Run(form);
        }

        /// <summary>
        /// Close the stub when the launcher process ends, so a crash cannot
        /// leave a game "running" with no way to stop it from the interface.
        /// </summary>
        private static void WatchParent(int pid, Form form)
        {
            var thread = new Thread(delegate()
            {
                try
                {
                    using (var parent = Process.GetProcessById(pid))
                    {
                        parent.WaitForExit();
                    }
                }
                catch { }

                try
                {
                    if (form != null && !form.IsDisposed)
                    {
                        form.BeginInvoke(new MethodInvoker(delegate
                        {
                            if (!form.IsDisposed) form.Close();
                        }));
                    }
                    else
                    {
                        Environment.Exit(0);
                    }
                }
                catch { Environment.Exit(0); }
            });

            thread.IsBackground = true;
            thread.Name = "zycord-parent-watch";
            thread.Start();
        }
    }

    internal static class Native
    {
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr handle);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWCP_ROUND = 2;
    }

    internal sealed class Options
    {
        public string Title = "Zycord session";
        public string AppId = "";
        public int ParentPid;
        public bool Hidden;

        // Set when the launcher already knows Discord does not list this file
        // name, so the window can say so instead of claiming detection.
        public bool Undetected;

        public static Options Parse(string[] argv)
        {
            var o = new Options();
            if (argv == null) return o;

            for (var i = 0; i < argv.Length; i++)
            {
                var a = argv[i];
                if (string.Equals(a, "--title", StringComparison.OrdinalIgnoreCase) && i + 1 < argv.Length)
                {
                    o.Title = argv[++i];
                }
                else if (string.Equals(a, "--app-id", StringComparison.OrdinalIgnoreCase) && i + 1 < argv.Length)
                {
                    o.AppId = argv[++i];
                }
                else if (string.Equals(a, "--parent", StringComparison.OrdinalIgnoreCase) && i + 1 < argv.Length)
                {
                    int.TryParse(argv[++i], out o.ParentPid);
                }
                else if (string.Equals(a, "--hidden", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase))
                {
                    o.Hidden = true;
                }
                else if (string.Equals(a, "--undetected", StringComparison.OrdinalIgnoreCase))
                {
                    o.Undetected = true;
                }
                else if (!a.StartsWith("--", StringComparison.Ordinal))
                {
                    o.Title = a;
                }
            }
            return o;
        }
    }

    internal sealed class SessionForm : Form
    {
        private static readonly Color Ink = Color.FromArgb(0x0C, 0x0C, 0x0E);
        private static readonly Color Panel = Color.FromArgb(0x17, 0x17, 0x1A);
        private static readonly Color Line = Color.FromArgb(0x2C, 0x2C, 0x31);
        private static readonly Color Ink1 = Color.FromArgb(0xE9, 0xE9, 0xEC);
        private static readonly Color Ink2 = Color.FromArgb(0x9A, 0x9A, 0xA1);
        private static readonly Color Accent = Color.FromArgb(0x57, 0xB8, 0xA2);

        private readonly Options _opts;
        private readonly System.Windows.Forms.Timer _tick;
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly Label _elapsed;
        private readonly Label _hint;

        public SessionForm(Options opts)
        {
            _opts = opts;

            Text = "Zycord - " + opts.Title;
            ClientSize = new Size(404, 188);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Ink;
            ForeColor = Ink1;
            Font = new Font("Segoe UI", 9f);
            ShowIcon = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var brand = new Label
            {
                Text = "ZYCORD",
                Font = new Font("Segoe UI Semibold", 8.5f),
                ForeColor = Accent,
                Location = new Point(22, 16),
                AutoSize = true
            };

            var name = new Label
            {
                Text = _opts.Title,
                Font = new Font("Segoe UI Semibold", 13f),
                ForeColor = Ink1,
                Location = new Point(22, 40),
                Size = new Size(360, 26),
                AutoEllipsis = true
            };

            _hint = new Label
            {
                Text = opts.Undetected
                    ? "Session active. Discord does not list this file name."
                    : "Session active. Discord can detect this game.",
                ForeColor = Ink2,
                Location = new Point(22, 70),
                Size = new Size(360, 20),
                AutoEllipsis = true
            };

            _elapsed = new Label
            {
                Text = "00:00:00",
                Font = new Font("Consolas", 12f),
                ForeColor = Ink1,
                Location = new Point(22, 100),
                AutoSize = true
            };

            var end = new Button
            {
                Text = "End session",
                FlatStyle = FlatStyle.Flat,
                BackColor = Panel,
                ForeColor = Ink1,
                Location = new Point(262, 132),
                Size = new Size(120, 32),
                Cursor = Cursors.Hand
            };
            end.FlatAppearance.BorderColor = Line;
            end.Click += delegate { Close(); };

            Controls.Add(brand);
            Controls.Add(name);
            Controls.Add(_hint);
            Controls.Add(_elapsed);
            Controls.Add(end);

            _clock.Start();
            _tick = new System.Windows.Forms.Timer { Interval = 500 };
            _tick.Tick += delegate
            {
                var t = _clock.Elapsed;
                _elapsed.Text = string.Format("{0:00}:{1:00}:{2:00}",
                    (int)t.TotalHours, t.Minutes, t.Seconds);
            };
            _tick.Start();

            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (e.CloseReason == CloseReason.WindowsShutDown) return;
                _tick.Stop();
            };
        }

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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var pen = new Pen(Line))
            {
                g.DrawLine(pen, 22, 94, ClientSize.Width - 22, 94);
            }
            using (var brush = new SolidBrush(Accent))
            {
                g.FillEllipse(brush, 22, 104, 8, 8);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _tick != null) _tick.Dispose();
            base.Dispose(disposing);
        }
    }
}
