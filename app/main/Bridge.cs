// The message bridge between the interface and the launcher.
//
// The interface never touches the filesystem or the process table directly.
// It posts a command with an id, and receives exactly one reply carrying the
// same id. Every argument is validated here before it reaches Sessions or the
// store, so a malformed message can only ever produce an error reply.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Zycord
{
    internal static class Bridge
    {
        private static WebView2 _view;
        private static Shell _shell;

        public static void Attach(WebView2 view) { _view = view; }
        public static void AttachShell(Shell shell) { _shell = shell; }

        public static void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Dictionary<string, object> msg = null;
            string id = null;

            try
            {
                msg = Store.Serialiser.Deserialize<Dictionary<string, object>>(e.WebMessageAsJson);
            }
            catch (Exception ex)
            {
                Log.Warn("malformed bridge message: " + ex.Message);
            }

            if (msg == null)
            {
                Reply(null, false, null, "The launcher could not read that message.");
                return;
            }

            // Messages the self test uses. They carry no command and are
            // harmless in normal use.
            var probe = Store.String(msg, "selftest", null);
            if (probe != null)
            {
                SelfTest.OnProbe(probe, Store.String(msg, "message", ""));
                return;
            }

            id = Store.String(msg, "id", null);
            var cmd = Store.String(msg, "cmd", "");
            var args = Store.Dict(msg.ContainsKey("args") ? msg["args"] : null);

            try
            {
                Dispatch(id, cmd, args);
            }
            catch (Exception ex)
            {
                Log.Error("command " + cmd + " failed: " + ex);
                Reply(id, false, null, ex.Message);
            }
        }

        private static void Dispatch(string id, string cmd, Dictionary<string, object> a)
        {
            switch (cmd)
            {
                case "boot": Boot(id); return;
                case "settings.save": SaveSettings(id, a); return;

                case "library.add": AddToLibrary(id, a); return;
                case "library.remove": RemoveFromLibrary(id, a); return;
                case "library.clear": ClearLibrary(id); return;

                case "custom.add": AddCustom(id, a); return;
                case "custom.remove": RemoveCustom(id, a); return;

                case "favorite.toggle": ToggleFavorite(id, a); return;

                case "session.start": StartSession(id, a); return;
                case "session.stop": Reply(id, true, Sessions.Stop(Store.String(a, "id", "")), null); return;
                case "session.stopAll":
                    Reply(id, true, new Dictionary<string, object> { { "stopped", Sessions.StopAll() } }, null);
                    return;
                case "session.list": Reply(id, true, ListSessions(), null); return;
                case "session.extend": Extend(id, a); return;

                case "history.list": Reply(id, true, History(), null); return;
                case "history.clear": ClearHistory(id); return;

                case "storage.stats": Reply(id, true, Stats(), null); return;
                case "storage.cleanup":
                    var result = Sessions.Cleanup();
                    result["diskUse"] = Sessions.DiskUse();
                    result["folders"] = Sessions.FolderCount();
                    Reply(id, true, result, null);
                    return;

                case "log.tail":
                    Reply(id, true, new Dictionary<string, object>
                    {
                        { "text", Log.Tail(Store.Int(a, "lines", 400)) },
                        { "file", Log.FilePath },
                        { "size", Log.Size },
                        { "count", Log.Count }
                    }, null);
                    return;
                case "log.export": ExportLog(id); return;
                case "log.clear":
                    Reply(id, true, new Dictionary<string, object> { { "removed", Log.Clear() } }, null);
                    return;

                case "shell.minimise":
                    if (_shell != null) _shell.Minimise();
                    Reply(id, true, null, null);
                    return;
                case "shell.maximise":
                    if (_shell != null) _shell.ToggleMaximise();
                    Reply(id, true, null, null);
                    return;
                case "shell.close":
                    if (_shell != null) _shell.Close();
                    Reply(id, true, null, null);
                    return;
                case "shell.hide":
                    if (_shell != null) _shell.HideToTray();
                    Reply(id, true, null, null);
                    return;
                case "shell.drag":
                    if (_shell != null) Native.BeginDrag(_shell.Handle);
                    Reply(id, true, null, null);
                    return;
                case "shell.state":
                    Reply(id, true, WindowState(), null);
                    return;
                case "shell.openFolder": OpenFolder(Store.String(a, "which", "data")); Reply(id, true, null, null); return;
                case "shell.openExternal": OpenExternal(Store.String(a, "url", "")); Reply(id, true, null, null); return;
                case "shell.clipboard":
                    try { Clipboard.SetText(Store.String(a, "text", "")); Reply(id, true, null, null); }
                    catch (Exception ex) { Reply(id, false, null, "The clipboard could not be written: " + ex.Message); }
                    return;

                case "app.reset": Reset(id, a); return;

                default:
                    Reply(id, false, null, "Unknown command.");
                    return;
            }
        }

        // ------------------------------------------------------------------ boot

        private static void Boot(string id)
        {
            var s = Settings.Current;
            var library = new List<object>();
            foreach (var g in s.Library)
            {
                library.Add(new Dictionary<string, object>
                {
                    { "id", g.Id }, { "name", g.Name }, { "exe", g.Exe }, { "icon", g.Icon ?? "" }
                });
            }

            var history = new List<object>();
            foreach (var h in s.History)
            {
                history.Add(new Dictionary<string, object>
                {
                    { "id", h.Id }, { "name", h.Name }, { "exe", h.Exe }, { "icon", h.Icon ?? "" },
                    { "started", h.Started }, { "stopped", h.Stopped },
                    { "seconds", h.Seconds }, { "reason", h.Reason ?? "" }
                });
            }

            Reply(id, true, new Dictionary<string, object>
            {
                { "version", AppInfo.Version },
                { "stubPresent", File.Exists(Sessions.StubPath) },
                { "dataDir", Paths.Data },
                { "logDir", Paths.Logs },
                { "logFile", Log.FilePath },
                { "gamesDir", Paths.Games },
                { "development", Ui.IsDevelopment },
                { "diskUse", Sessions.DiskUse() },
                { "folders", Sessions.FolderCount() },
                { "library", library },
                { "custom", CustomJson(s) },
                { "favorites", new List<string>(s.Favorites) },
                { "recent", new List<string>(s.Recent) },
                { "history", history },
                { "window", WindowState() },
                { "settings", SettingsJson(s) }
            }, null);
        }

        private static Dictionary<string, object> SettingsJson(Settings s)
        {
            return new Dictionary<string, object>
            {
                { "minimiseToTray", s.MinimiseToTray },
                { "showSessionWindow", s.ShowSessionWindow },
                { "confirmBeforeStop", s.ConfirmBeforeStop },
                { "confirmOnStart", s.ConfirmOnStart },
                { "keepOnTop", s.KeepOnTop },
                { "startMinimised", s.StartMinimised },
                { "reduceMotion", s.ReduceMotion },
                { "autoStopMinutes", s.AutoStopMinutes },
                { "theme", s.Theme }
            };
        }

        private static Dictionary<string, object> WindowState()
        {
            var max = _shell != null && _shell.WindowState == FormWindowState.Maximized;
            return new Dictionary<string, object> { { "maximised", max } };
        }

        private static void SaveSettings(string id, Dictionary<string, object> a)
        {
            if (a == null) { Reply(id, false, null, "No settings were supplied."); return; }

            var s = Settings.Current;
            s.MinimiseToTray = Store.Bool(a, "minimiseToTray", s.MinimiseToTray);
            s.ShowSessionWindow = Store.Bool(a, "showSessionWindow", s.ShowSessionWindow);
            s.ConfirmBeforeStop = Store.Bool(a, "confirmBeforeStop", s.ConfirmBeforeStop);
            s.ConfirmOnStart = Store.Bool(a, "confirmOnStart", s.ConfirmOnStart);
            s.KeepOnTop = Store.Bool(a, "keepOnTop", s.KeepOnTop);
            s.StartMinimised = Store.Bool(a, "startMinimised", s.StartMinimised);
            s.ReduceMotion = Store.Bool(a, "reduceMotion", s.ReduceMotion);

            var auto = Store.Int(a, "autoStopMinutes", s.AutoStopMinutes);
            if (auto < 0) auto = 0;
            if (auto > 240) auto = 240;
            s.AutoStopMinutes = auto;

            var theme = Store.String(a, "theme", s.Theme);
            s.Theme = theme == "light" ? "light" : "glass";

            Store.Save();

            if (_shell != null)
            {
                _shell.TopMost = s.KeepOnTop;
                _shell.ApplyTheme();
            }

            Reply(id, true, SettingsJson(s), null);
        }

        // --------------------------------------------------------------- library

        private static void AddToLibrary(string id, Dictionary<string, object> a)
        {
            var appId = Store.String(a, "id", "");
            var name = Store.String(a, "name", "");
            var exe = Store.String(a, "exe", "");
            var icon = Store.String(a, "icon", "");

            if (!IsAppId(appId)) { Reply(id, false, null, "That application id is not valid."); return; }
            if (name.Length > 200) name = name.Substring(0, 200);
            if (Sessions.UnsafeName.IsMatch(name))
            {
                Reply(id, false, null, "That game name contains characters that cannot be used.");
                return;
            }
            if (string.IsNullOrWhiteSpace(name)) { Reply(id, false, null, "A game name is required."); return; }

            var safe = Sessions.SafeRelativeExe(exe);
            if (safe == null) { Reply(id, false, null, "That executable path cannot be used."); return; }

            var s = Settings.Current;
            s.Library.RemoveAll(delegate(SavedGame g) { return g.Id == appId; });
            s.Library.Insert(0, new SavedGame
            {
                Id = appId,
                Name = name,
                Exe = safe,
                Icon = icon,
                Added = DateTime.UtcNow.ToString("o")
            });
            Store.Save();

            Reply(id, true, new Dictionary<string, object> { { "count", s.Library.Count } }, null);
        }

        private static void RemoveFromLibrary(string id, Dictionary<string, object> a)
        {
            var appId = Store.String(a, "id", "");
            var s = Settings.Current;
            var removed = s.Library.RemoveAll(delegate(SavedGame g) { return g.Id == appId; });
            Store.Save();
            Reply(id, true, new Dictionary<string, object> { { "removed", removed } }, null);
        }

        private static void ClearLibrary(string id)
        {
            var s = Settings.Current;
            var removed = s.Library.Count;
            s.Library.Clear();
            Store.Save();
            Reply(id, true, new Dictionary<string, object> { { "removed", removed } }, null);
        }

        private static void ToggleFavorite(string id, Dictionary<string, object> a)
        {
            var appId = Store.String(a, "id", "");
            if (!IsAppId(appId)) { Reply(id, false, null, "That application id is not valid."); return; }
            var now = Store.ToggleFavorite(appId);
            Reply(id, true, new Dictionary<string, object> { { "favorite", now } }, null);
        }

        // --------------------------------------------------------------- custom

        private static List<object> CustomJson(Settings s)
        {
            var list = new List<object>();
            foreach (var g in s.Custom)
            {
                list.Add(new Dictionary<string, object>
                {
                    { "id", g.Id }, { "name", g.Name }, { "exe", g.Exe },
                    { "icon", g.Icon ?? "" }, { "added", g.Added ?? "" }
                });
            }
            return list;
        }

        private static void AddCustom(string id, Dictionary<string, object> a)
        {
            var name = Store.String(a, "name", "").Trim();
            var exe = Store.String(a, "exe", "").Trim();
            var icon = Store.String(a, "icon", "").Trim();

            if (!Sessions.SafeName(name))
            {
                Reply(id, false, null,
                    "A game name is required, at most 200 characters, and free of control characters.");
                return;
            }

            var safe = Sessions.SafeRelativeExe(exe);
            if (safe == null)
            {
                Reply(id, false, null,
                    "The executable must be a relative path ending in .exe, .bin, .dat, .dll, .des, .jbg, .game or .pae.");
                return;
            }

            if (Settings.Current.Custom.Count >= Store.MaxCustom)
            {
                Reply(id, false, null, "The custom game list is full. Remove one before adding another.");
                return;
            }

            // A name Discord already lists would be confusing to remove later,
            // so a duplicate name is refused rather than silently shadowing.
            var existing = Settings.Current.Custom.Find(
                delegate(SavedGame g) { return string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase); });
            if (existing != null)
            {
                Reply(id, false, null, "A custom game called " + existing.Name + " already exists.");
                return;
            }

            var entry = new SavedGame
            {
                Id = Store.NextCustomId(),
                Name = name,
                Exe = safe,
                Icon = icon,
                Added = DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture)
            };
            Store.AddCustom(entry);
            Log.Info("custom game added: " + entry.Name + " (" + entry.Id + ")");

            Reply(id, true, new Dictionary<string, object>
            {
                { "id", entry.Id }, { "name", entry.Name }, { "exe", entry.Exe },
                { "icon", entry.Icon }, { "added", entry.Added }
            }, null);
        }

        private static void RemoveCustom(string id, Dictionary<string, object> a)
        {
            var appId = Store.String(a, "id", "");
            if (!Store.IsCustomId(appId)) { Reply(id, false, null, "That is not a custom game."); return; }

            var removed = Store.RemoveCustom(appId);
            if (!removed) { Reply(id, false, null, "That custom game is not in the list."); return; }

            Log.Info("custom game removed: " + appId);
            Reply(id, true, new Dictionary<string, object> { { "removed", true } }, null);
        }

        // -------------------------------------------------------------- sessions

        private static void StartSession(string id, Dictionary<string, object> a)
        {
            var appId = Store.String(a, "id", "");
            var name = Store.String(a, "name", "");
            var exe = Store.String(a, "exe", "");
            var icon = Store.String(a, "icon", "");
            var detected = Store.Bool(a, "detected", true);
            Reply(id, true, Sessions.Start(appId, name, exe, icon, detected), null);
        }

        private static void Extend(string id, Dictionary<string, object> a)
        {
            var appId = Store.String(a, "id", "");
            var minutes = Store.Int(a, "minutes", 0);
            if (minutes < 1) minutes = 1;
            if (minutes > 240) minutes = 240;

            var updated = Sessions.Extend(appId, minutes);
            if (updated == null) { Reply(id, false, null, "That session is not running."); return; }
            Reply(id, true, updated, null);
        }

        private static List<object> ListSessions()
        {
            var list = new List<object>();
            foreach (var s in Sessions.Snapshot()) list.Add(s.ToJson());
            return list;
        }

        private static List<object> History()
        {
            var list = new List<object>();
            foreach (var h in Settings.Current.History)
            {
                list.Add(new Dictionary<string, object>
                {
                    { "id", h.Id }, { "name", h.Name }, { "exe", h.Exe }, { "icon", h.Icon ?? "" },
                    { "started", h.Started }, { "stopped", h.Stopped },
                    { "seconds", h.Seconds }, { "reason", h.Reason ?? "" }
                });
            }
            return list;
        }

        private static void ClearHistory(string id)
        {
            var s = Settings.Current;
            var removed = s.History.Count;
            s.History.Clear();
            Store.Save();
            Reply(id, true, new Dictionary<string, object> { { "removed", removed } }, null);
        }

        // --------------------------------------------------------------- storage

        private static Dictionary<string, object> Stats()
        {
            return new Dictionary<string, object>
            {
                { "diskUse", Sessions.DiskUse() },
                { "folders", Sessions.FolderCount() },
                { "logSize", Log.Size },
                { "logCount", Log.Count }
            };
        }

        // ------------------------------------------------------------------ logs

        private static void ExportLog(string id)
        {
            try
            {
                using (var dialog = new SaveFileDialog())
                {
                    dialog.Title = "Export Zycord log";
                    dialog.Filter = "Text file (*.txt)|*.txt|All files (*.*)|*.*";
                    dialog.FileName = "zycord-log-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".txt";
                    dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

                    if (dialog.ShowDialog() != DialogResult.OK)
                    {
                        Reply(id, true, new Dictionary<string, object> { { "saved", false } }, null);
                        return;
                    }

                    var ok = Log.Export(dialog.FileName);
                    Reply(id, ok, ok
                        ? new Dictionary<string, object> { { "saved", true }, { "path", dialog.FileName } }
                        : null,
                        ok ? null : "The log could not be written to that location.");
                }
            }
            catch (Exception ex)
            {
                Reply(id, false, null, "The log could not be exported: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------- shell

        private static void OpenFolder(string which)
        {
            string target;
            if (string.Equals(which, "logs", StringComparison.OrdinalIgnoreCase)) target = Paths.Logs;
            else if (string.Equals(which, "games", StringComparison.OrdinalIgnoreCase)) target = Paths.Games;
            else target = Paths.Data;

            try
            {
                Directory.CreateDirectory(target);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + target + "\"")
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { Log.Warn("open folder failed: " + ex.Message); }
        }

        private static void OpenExternal(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url)) return;
                if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return;

                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Warn("open url failed: " + ex.Message); }
        }

        private static void Reset(string id, Dictionary<string, object> a)
        {
            var what = Store.String(a, "what", "");
            var s = Settings.Current;

            switch (what)
            {
                case "history":
                    s.History.Clear();
                    s.Recent.Clear();
                    break;
                case "library":
                    s.Library.Clear();
                    s.Favorites.Clear();
                    break;
                case "custom":
                    s.Custom.Clear();
                    break;
                case "settings":
                    var library = s.Library;
                    var favorites = s.Favorites;
                    var recent = s.Recent;
                    var history = s.History;
                    var fresh = new Settings();
                    fresh.Library = library;
                    fresh.Favorites = favorites;
                    fresh.Recent = recent;
                    fresh.History = history;
                    s.MinimiseToTray = fresh.MinimiseToTray;
                    s.ShowSessionWindow = fresh.ShowSessionWindow;
                    s.ConfirmBeforeStop = fresh.ConfirmBeforeStop;
                    s.ConfirmOnStart = fresh.ConfirmOnStart;
                    s.KeepOnTop = fresh.KeepOnTop;
                    s.StartMinimised = fresh.StartMinimised;
                    s.ReduceMotion = fresh.ReduceMotion;
                    s.AutoStopMinutes = fresh.AutoStopMinutes;
                    s.Theme = fresh.Theme;
                    break;
                default:
                    Reply(id, false, null, "Nothing to reset.");
                    return;
            }

            Store.Save();
            if (_shell != null)
            {
                _shell.TopMost = s.KeepOnTop;
                _shell.ApplyTheme();
            }
            Log.Info("reset " + what);
            Reply(id, true, SettingsJson(s), null);
        }

        // ----------------------------------------------------------------- utils

        private static bool IsAppId(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 25) return false;
            foreach (var c in value)
            {
                if (c < '0' || c > '9') return false;
            }
            return true;
        }

        private static void Reply(string id, bool ok, object data, string error)
        {
            var payload = new Dictionary<string, object> { { "id", id }, { "ok", ok } };
            if (data != null) payload["data"] = data;
            if (error != null) payload["error"] = error;
            Post(payload);
        }

        public static void Post(object payload)
        {
            try
            {
                if (_view == null || _view.CoreWebView2 == null) return;
                _view.CoreWebView2.PostWebMessageAsJson(Store.Serialiser.Serialize(payload));
            }
            catch (Exception ex)
            {
                Log.Warn("reply failed: " + ex.Message);
            }
        }

        /// <summary>Push the current session list to the interface.</summary>
        public static void PushSessions()
        {
            Post(new Dictionary<string, object>
            {
                { "event", "sessions" },
                { "data", ListSessions() }
            });
        }

        public static void PushWindowState()
        {
            Post(new Dictionary<string, object>
            {
                { "event", "window" },
                { "data", WindowState() }
            });
        }

        public static void PushEvent(string name, object data)
        {
            Post(new Dictionary<string, object> { { "event", name }, { "data", data } });
        }
    }
}
