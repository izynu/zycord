// Zycord launcher: entry point, application paths, logging and the single
// instance guard.
//
// The launcher is a single WinForms executable that hosts the interface in
// WebView2. Everything privileged lives on this side of the bridge: the
// interface can only ask for the commands Bridge.cs declares.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Zycord
{
    internal static class AppInfo
    {
        public const string Name = "Zycord";
        public const string Repo = "https://github.com/iZynu/zycord";

        public static string Version
        {
            get
            {
                try
                {
                    var v = typeof(AppInfo).Assembly.GetName().Version;
                    return v.Major + "." + v.Minor + "." + v.Build;
                }
                catch { return "1.1.1"; }
            }
        }
    }

    internal static class Entry
    {
        private const string MutexName = "Local\\Zycord.SingleInstance.1";

        [STAThread]
        private static void Main(string[] argv)
        {
            var cli = CommandLine.Parse(argv);
            try { Native.EnableDpiAwareness(); } catch { }

            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Log.Error("unhandled: " + e.ExceptionObject);
            };
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                Log.Error("ui thread: " + e.Exception);
            };

            if (cli.Version)
            {
                Console.WriteLine(AppInfo.Name + " " + AppInfo.Version);
                return;
            }

            if (cli.Headless)
            {
                Environment.Exit(SelfTest.Run());
                return;
            }

            bool created;
            using (var guard = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    // A second launch is a request to show the window that is
                    // already open, not a second copy of the application.
                    Log.Info("second instance: handing over to the running window");
                    Native.BroadcastShow();
                    return;
                }

                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new Shell(cli));
                }
                finally
                {
                    try { guard.ReleaseMutex(); } catch { }
                }
            }
        }
    }

    internal sealed class CommandLine
    {
        public bool Headless;
        public bool DevTools;
        public bool Version;

        public static CommandLine Parse(string[] argv)
        {
            var c = new CommandLine();
            if (argv == null) return c;
            foreach (var a in argv)
            {
                if (string.Equals(a, "--headless", StringComparison.OrdinalIgnoreCase)) c.Headless = true;
                else if (string.Equals(a, "--devtools", StringComparison.OrdinalIgnoreCase)) c.DevTools = true;
                else if (string.Equals(a, "--version", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "-v", StringComparison.OrdinalIgnoreCase)) c.Version = true;
            }
            return c;
        }
    }

    internal static class Native
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint RegisterWindowMessage(string name);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);

        [DllImport("user32.dll")] public static extern bool ReleaseCapture();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);

        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWCP_ROUND = 2;
        public const int SW_RESTORE = 9;
        public const int WM_NCLBUTTONDOWN = 0x00A1;
        public const int HTCAPTION = 2;

        private static readonly IntPtr DpiAwarenessPerMonitorV2 = new IntPtr(-4);
        private static readonly IntPtr BroadcastHandle = new IntPtr(0xFFFF);
        private static uint _showMessage;

        public static uint ShowMessage
        {
            get
            {
                if (_showMessage == 0) _showMessage = RegisterWindowMessage("Zycord.ShowWindow.1");
                return _showMessage;
            }
        }

        /// <summary>Per-monitor v2 where it exists, system aware everywhere else.</summary>
        public static void EnableDpiAwareness()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(DpiAwarenessPerMonitorV2)) return;
            }
            catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        /// <summary>Ask whichever Zycord window is running to come to the front.</summary>
        public static void BroadcastShow()
        {
            var msg = ShowMessage;
            if (msg == 0) return;
            for (var i = 0; i < 6; i++)
            {
                PostMessage(BroadcastHandle, msg, IntPtr.Zero, IntPtr.Zero);
                Thread.Sleep(120);
            }
        }

        /// <summary>Start a window drag from the interface's title bar.</summary>
        public static void BeginDrag(IntPtr handle)
        {
            try
            {
                ReleaseCapture();
                SendMessage(handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
            }
            catch { }
        }
    }

    internal static class Paths
    {
        public static string Data
        {
            get
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var p = Path.Combine(root, AppInfo.Name);
                Directory.CreateDirectory(p);
                return p;
            }
        }

        public static string Logs { get { return Ensure("logs"); } }
        public static string WebView { get { return Ensure("webview"); } }
        public static string Games { get { return Ensure("games"); } }
        public static string Settings { get { return Path.Combine(Data, "settings.json"); } }

        public static string ExeDir
        {
            get { return Path.GetDirectoryName(Application.ExecutablePath); }
        }

        private static string Ensure(string name)
        {
            var p = Path.Combine(Data, name);
            Directory.CreateDirectory(p);
            return p;
        }
    }

    internal static class Log
    {
        private static readonly object Gate = new object();
        private static string _day;
        private static string _path;
        private const long MaxBytes = 4L * 1024 * 1024;

        public static string FilePath
        {
            get { lock (Gate) { Roll(); return _path; } }
        }

        public static void Info(string m) { Write("INFO ", m); }
        public static void Warn(string m) { Write("WARN ", m); }
        public static void Error(string m) { Write("ERROR", m); }

        private static void Roll()
        {
            var day = DateTime.Now.ToString("yyyyMMdd");
            if (_day == day && _path != null) return;
            _day = day;
            _path = Path.Combine(Paths.Logs, "zycord-" + day + ".log");
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    Roll();
                    try
                    {
                        if (File.Exists(_path) && new FileInfo(_path).Length > MaxBytes)
                        {
                            var old = _path + ".1";
                            if (File.Exists(old)) File.Delete(old);
                            File.Move(_path, old);
                        }
                    }
                    catch { }

                    File.AppendAllText(_path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + level + " " + message +
                        Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch { }
        }

        /// <summary>The last N lines of today's file, oldest first.</summary>
        public static string Tail(int lines)
        {
            if (lines < 1) lines = 1;
            if (lines > 4000) lines = 4000;

            try
            {
                string path;
                lock (Gate) { Roll(); path = _path; }
                if (!File.Exists(path)) return "";

                var all = ReadShared(path);
                var start = Math.Max(0, all.Length - lines);
                var sb = new StringBuilder();
                for (var i = start; i < all.Length; i++) sb.AppendLine(all[i]);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "Could not read the log: " + ex.Message;
            }
        }

        /// <summary>Reads a file another process may still be writing to.</summary>
        private static string[] ReadShared(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                return reader.ReadToEnd().Replace("\r\n", "\n").Split('\n');
            }
        }

        public static long Size
        {
            get
            {
                try
                {
                    string path;
                    lock (Gate) { Roll(); path = _path; }
                    return File.Exists(path) ? new FileInfo(path).Length : 0;
                }
                catch { return 0; }
            }
        }

        public static int Count
        {
            get
            {
                try { return Directory.GetFiles(Paths.Logs, "*.log").Length; }
                catch { return 0; }
            }
        }

        public static bool Export(string destination)
        {
            try
            {
                string path;
                lock (Gate) { Roll(); path = _path; }
                var text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
                var header =
                    AppInfo.Name + " " + AppInfo.Version + Environment.NewLine +
                    "Exported " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine +
                    new string('-', 60) + Environment.NewLine;
                File.WriteAllText(destination, header + text, new UTF8Encoding(false));
                Info("log exported to " + destination);
                return true;
            }
            catch (Exception ex)
            {
                Warn("log export failed: " + ex.Message);
                return false;
            }
        }

        public static int Clear()
        {
            var removed = 0;
            try
            {
                lock (Gate)
                {
                    foreach (var f in Directory.GetFiles(Paths.Logs, "*.log*"))
                    {
                        try { File.Delete(f); removed++; } catch { }
                    }
                    _day = null;
                    _path = null;
                }
            }
            catch { }
            return removed;
        }
    }
}
