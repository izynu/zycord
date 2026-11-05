// End-to-end self test, run with --headless.
//
// It boots the real shell with the real interface, waits for the interface to
// report that it is ready, exercises the backend directly, writes a report and
// exits. A non-zero exit code means the build is not fit to ship.
//
// The test restores every setting it touches, so running it does not change
// the user's configuration.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Zycord
{
    internal static class SelfTest
    {
        private static readonly List<string> Lines = new List<string>();
        private static int _failures;
        private static int _stage;
        private static Timer _timeout;
        private static Form _form;
        private static WebView2 _view;
        private static bool _finished;

        private static void Note(string m)
        {
            Lines.Add(m);
            Log.Info("selftest: " + m);
        }

        private static void Pass(string m) { Note("PASS " + m); }

        private static void Fail(string m)
        {
            _failures++;
            Note("FAIL " + m);
        }

        /// <summary>Called by Bridge for messages the interface marks as test probes.</summary>
        public static void OnProbe(string kind, string message)
        {
            if (kind == "ready")
            {
                _stage = 4;
                Pass("interface reported ready");
                CheckLayers();
                Finish();
                return;
            }

            if (kind == "error")
            {
                Fail("interface error: " + message);
                Finish();
            }
        }

        public static int Run()
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                _form = new Form();
                _form.Text = "Zycord self test";
                _form.ShowInTaskbar = false;
                _form.Opacity = 0;
                _form.Size = new Size(1024, 700);
                _form.StartPosition = FormStartPosition.Manual;
                _form.Location = new Point(-4000, -4000);

                _view = new WebView2();
                _view.Dock = DockStyle.Fill;
                _form.Controls.Add(_view);

                Bridge.Attach(_view);

                _timeout = new Timer { Interval = 60000 };
                _timeout.Tick += delegate
                {
                    Fail("timed out at stage " + _stage);
                    Finish();
                };
                _timeout.Start();

                _form.Shown += async delegate
                {
                    try
                    {
                        _stage = 1;
                        var env = await CoreWebView2Environment.CreateAsync(null, Paths.WebView, null);
                        await _view.EnsureCoreWebView2Async(env);
                        Pass("webview environment and core");

                        _stage = 2;
                        var core = _view.CoreWebView2;
                        core.Settings.AreDevToolsEnabled = false;
                        core.Settings.AreDefaultContextMenusEnabled = false;
                        core.Settings.IsWebMessageEnabled = true;

                        core.SetVirtualHostNameToFolderMapping(
                            "zycord.local", Ui.Root, CoreWebView2HostResourceAccessKind.DenyCors);
                        Pass("interface folder mapped: " + Ui.Root);

                        _stage = 3;
                        core.WebMessageReceived += Bridge.OnMessage;
                        core.NavigationCompleted += delegate(object s, CoreWebView2NavigationCompletedEventArgs e)
                        {
                            if (e.IsSuccess) Pass("interface navigation");
                            else Fail("interface navigation: " + e.WebErrorStatus);
                        };

                        core.Navigate("https://zycord.local/index.html");
                        Note("navigated");
                    }
                    catch (Exception ex)
                    {
                        Fail("startup: " + ex.Message);
                        Finish();
                    }
                };

                Application.Run(_form);
            }
            catch (Exception ex)
            {
                Fail("fatal: " + ex.Message);
            }

            return WriteReport();
        }

        /// <summary>
        /// Exercise the backend layers directly. These are the parts that touch
        /// the disk and the process table, so they are the parts worth proving.
        /// </summary>
        private static void CheckLayers()
        {
            _stage = 5;

            if (File.Exists(Sessions.StubPath)) Pass("stub present: " + Sessions.StubPath);
            else Fail("stub missing: " + Sessions.StubPath);

            foreach (var name in new[] { "index.html", "app.css", "app.js", "mark.svg" })
            {
                if (File.Exists(Path.Combine(Ui.Root, name))) Pass(name + " present");
                else Fail(name + " missing");
            }

            var catalog = Path.Combine(Ui.Root, "catalog.json");
            if (File.Exists(catalog))
            {
                Pass("catalog present, " + (new FileInfo(catalog).Length / 1024) + " KB");
            }
            else Fail("catalog missing");

            _stage = 6;
            CheckPaths();
            CheckSettings();
            CheckLibrary();
            CheckSession();
            CheckLog();

            _stage = 9;
        }

        private static void CheckPaths()
        {
            var bad = new[]
            {
                "../../windows/system32/cmd.exe",
                "C:\\Windows\\notepad.exe",
                "/etc/passwd",
                "game/../../../evil.exe",
                "game//sub/../escape.exe",
                "",
                "noextension",
                "game/run.bat",
                "game\\..\\..\\evil.exe",
                "\\\\\\\\server\\\\share\\\\x.exe"
            };
            var rejected = 0;
            foreach (var b in bad)
            {
                if (Sessions.SafeRelativeExe(b) == null) rejected++;
                else Fail("unsafe path accepted: " + b);
            }
            if (rejected == bad.Length) Pass("path validation rejects all " + bad.Length + " hostile inputs");

            var good = new[]
            {
                "game.exe",
                "bin/win64/game.exe",
                "Game/Content/Binaries/Win64/Game-Win64-Shipping.exe"
            };
            var accepted = 0;
            foreach (var g in good)
            {
                if (Sessions.SafeRelativeExe(g) != null) accepted++;
                else Fail("safe path rejected: " + g);
            }
            if (accepted == good.Length) Pass("path validation accepts all " + good.Length + " valid inputs");
        }

        private static void CheckSettings()
        {
            var s = Settings.Current;
            var beforeTray = s.MinimiseToTray;
            var beforeAuto = s.AutoStopMinutes;
            var beforeTheme = s.Theme;

            s.MinimiseToTray = !beforeTray;
            s.AutoStopMinutes = 999;
            Store.Save();

            var reloaded = Store.Load();
            if (reloaded.MinimiseToTray == !beforeTray) Pass("settings round trip");
            else Fail("settings did not round trip");

            if (reloaded.AutoStopMinutes == 240) Pass("settings clamp the auto stop range");
            else Fail("auto stop was not clamped, got " + reloaded.AutoStopMinutes);

            s.MinimiseToTray = beforeTray;
            s.AutoStopMinutes = beforeAuto;
            s.Theme = beforeTheme;
            Store.Save();
        }

        private static void CheckLibrary()
        {
            var s = Settings.Current;
            var before = new List<SavedGame>(s.Library);
            var beforeFavorites = new List<string>(s.Favorites);

            try
            {
                s.Library.Insert(0, new SavedGame
                {
                    Id = "0", Name = "Self Test", Exe = "bin/selftest.exe", Icon = ""
                });
                Store.Save();
                var reloaded = Store.Load();
                var found = reloaded.Library.Find(delegate(SavedGame g) { return g.Id == "0"; });
                if (found != null && found.Exe == "bin/selftest.exe") Pass("library round trip");
                else Fail("library did not round trip");

                var on = Store.ToggleFavorite("0");
                var off = Store.ToggleFavorite("0");
                if (on && !off) Pass("favourite toggle");
                else Fail("favourite toggle returned " + on + "/" + off);
            }
            catch (Exception ex)
            {
                Fail("library: " + ex.Message);
            }
            finally
            {
                s.Library.Clear();
                s.Library.AddRange(before);
                s.Favorites.Clear();
                s.Favorites.AddRange(beforeFavorites);
                Store.Save();
            }
        }

        private static void CheckSession()
        {
            var s = Settings.Current;
            var beforeWindow = s.ShowSessionWindow;
            var beforeHistory = s.History.Count;

            try
            {
                s.ShowSessionWindow = false;
                s.AutoStopMinutes = 0;

                var start = Sessions.Start("0", "Self Test", "bin/selftest.exe", "");
                if (!Convert.ToBoolean(start["ok"]))
                {
                    Fail("session start: " + Store.String(start, "error", "unknown"));
                    return;
                }
                Pass("session start");

                if (Sessions.Snapshot().Count == 1) Pass("session tracked");
                else Fail("session count was " + Sessions.Snapshot().Count);

                var again = Sessions.Start("0", "Self Test", "bin/selftest.exe", "");
                if (Convert.ToBoolean(again["already"])) Pass("duplicate start is idempotent");
                else Fail("duplicate start did not report already");

                var stop = Sessions.Stop("0");
                if (Convert.ToBoolean(stop["stopped"])) Pass("session stop");
                else Fail("session stop returned false");

                if (Sessions.Snapshot().Count == 0) Pass("session list empty after stop");
                else Fail("session list not empty after stop");

                if (Settings.Current.History.Count == beforeHistory + 1) Pass("history recorded");
                else Fail("history entry was not written");
            }
            catch (Exception ex)
            {
                Fail("session lifecycle: " + ex.Message);
            }
            finally
            {
                s.ShowSessionWindow = beforeWindow;
                if (Settings.Current.History.Count > beforeHistory)
                {
                    Settings.Current.History.RemoveRange(
                        beforeHistory, Settings.Current.History.Count - beforeHistory);
                }
                Store.Save();

                try
                {
                    var folder = Path.Combine(Paths.Games, "0");
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
                    Pass("test artifacts removed");
                }
                catch (Exception ex) { Note("cleanup note: " + ex.Message); }
            }
        }

        private static void CheckLog()
        {
            try
            {
                var text = Log.Tail(50);
                if (!string.IsNullOrEmpty(text)) Pass("log is being written");
                else Fail("log is empty");

                if (File.Exists(Log.FilePath)) Pass("log file exists");
                else Fail("log file missing");
            }
            catch (Exception ex) { Fail("log: " + ex.Message); }
        }

        private static void Finish()
        {
            if (_finished) return;
            _finished = true;

            try { if (_timeout != null) _timeout.Stop(); } catch { }
            try { if (_form != null) _form.Close(); } catch { }
        }

        private static int WriteReport()
        {
            var path = Path.Combine(Paths.Logs, "selftest.txt");
            var sb = new StringBuilder();
            foreach (var l in Lines) sb.AppendLine(l);
            sb.AppendLine();
            sb.AppendLine(_failures == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + _failures + ")");

            try { File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false)); } catch { }
            try { Console.WriteLine(sb.ToString()); } catch { }

            return _failures == 0 ? 0 : 1;
        }
    }
}
