// Persisted preferences, the saved game library, favourites, recents and
// session history.
//
// All of it lives in one JSON document under %LOCALAPPDATA%\Zycord. Writes are
// atomic: a temporary file is written and then moved over the original, so an
// interrupted save can never leave a truncated file behind. Timestamps are
// stored as ISO-8601 strings rather than DateTime, because the serialiser's
// date format is easy to get wrong and a string is unambiguous.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace Zycord
{
    internal sealed class SavedGame
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Exe { get; set; }
        public string Icon { get; set; }
        public string Added { get; set; }
    }

    internal sealed class HistoryEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Exe { get; set; }
        public string Icon { get; set; }
        public string Started { get; set; }
        public string Stopped { get; set; }
        public int Seconds { get; set; }
        public string Reason { get; set; }
    }

    internal sealed class Settings
    {
        private static Settings _current;
        private static readonly object Gate = new object();

        public static Settings Current
        {
            get
            {
                lock (Gate)
                {
                    if (_current == null) _current = Store.Load();
                    return _current;
                }
            }
        }

        public bool MinimiseToTray { get; set; }
        public bool ShowSessionWindow { get; set; }
        public bool ConfirmBeforeStop { get; set; }
        public bool ConfirmOnStart { get; set; }
        public bool KeepOnTop { get; set; }
        public bool StartMinimised { get; set; }
        public bool ReduceMotion { get; set; }

        /// <summary>0 disables the automatic stop timer. Otherwise 1-240 minutes.</summary>
        public int AutoStopMinutes { get; set; }

        /// <summary>"glass" or "light".</summary>
        public string Theme { get; set; }

        public List<SavedGame> Library { get; set; }
        public List<string> Favorites { get; set; }
        public List<string> Recent { get; set; }
        public List<HistoryEntry> History { get; set; }

        public int WindowX { get; set; }
        public int WindowY { get; set; }
        public int WindowW { get; set; }
        public int WindowH { get; set; }
        public bool WindowMax { get; set; }

        public Settings()
        {
            MinimiseToTray = true;
            ShowSessionWindow = true;
            ConfirmBeforeStop = false;
            ConfirmOnStart = false;
            KeepOnTop = false;
            StartMinimised = false;
            ReduceMotion = false;
            AutoStopMinutes = 0;
            Theme = "glass";
            Library = new List<SavedGame>();
            Favorites = new List<string>();
            Recent = new List<string>();
            History = new List<HistoryEntry>();
            WindowW = 1180;
            WindowH = 780;
        }
    }

    internal static class Store
    {
        public const int MaxHistory = 250;
        public const int MaxRecent = 12;

        private static readonly object Gate = new object();
        private static readonly JavaScriptSerializer Json = Create();

        private static JavaScriptSerializer Create()
        {
            var s = new JavaScriptSerializer();
            s.MaxJsonLength = 32 * 1024 * 1024;
            s.RecursionLimit = 100;
            return s;
        }

        public static JavaScriptSerializer Serialiser { get { return Json; } }

        public static Settings Load()
        {
            var fallback = new Settings();
            try
            {
                if (!File.Exists(Paths.Settings)) return fallback;
                var text = File.ReadAllText(Paths.Settings, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) return fallback;

                var s = Json.Deserialize<Settings>(text);
                if (s == null) return fallback;

                Normalise(s);
                return s;
            }
            catch (Exception ex)
            {
                Log.Warn("settings unreadable, using defaults: " + ex.Message);
                return fallback;
            }
        }

        /// <summary>
        /// Repair anything a hand-edited or older settings file could contain.
        /// Every value is clamped to a range the rest of the code can rely on.
        /// </summary>
        private static void Normalise(Settings s)
        {
            if (s.Library == null) s.Library = new List<SavedGame>();
            s.Library.RemoveAll(delegate(SavedGame g)
            {
                return g == null || string.IsNullOrEmpty(g.Id) || string.IsNullOrEmpty(g.Exe) ||
                       string.IsNullOrEmpty(g.Name);
            });

            if (s.Favorites == null) s.Favorites = new List<string>();
            s.Favorites.RemoveAll(delegate(string id) { return string.IsNullOrEmpty(id); });

            if (s.Recent == null) s.Recent = new List<string>();
            s.Recent.RemoveAll(delegate(string id) { return string.IsNullOrEmpty(id); });
            if (s.Recent.Count > MaxRecent) s.Recent.RemoveRange(MaxRecent, s.Recent.Count - MaxRecent);

            if (s.History == null) s.History = new List<HistoryEntry>();
            s.History.RemoveAll(delegate(HistoryEntry h) { return h == null || string.IsNullOrEmpty(h.Id); });
            if (s.History.Count > MaxHistory) s.History.RemoveRange(MaxHistory, s.History.Count - MaxHistory);

            if (s.AutoStopMinutes < 0) s.AutoStopMinutes = 0;
            if (s.AutoStopMinutes > 240) s.AutoStopMinutes = 240;

            if (s.Theme != "light") s.Theme = "glass";

            if (s.WindowW < 900) s.WindowW = 900;
            if (s.WindowW > 6000) s.WindowW = 1180;
            if (s.WindowH < 600) s.WindowH = 600;
            if (s.WindowH > 4000) s.WindowH = 780;
        }

        public static void Save()
        {
            lock (Gate)
            {
                WriteAtomic(Paths.Settings, Json.Serialize(Settings.Current));
            }
        }

        /// <summary>Save without letting a disk error take the interface down.</summary>
        public static void SaveQuiet()
        {
            try { Save(); }
            catch (Exception ex) { Log.Warn("settings save failed: " + ex.Message); }
        }

        public static void WriteAtomic(string path, string contents)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, contents, new UTF8Encoding(false));

            if (File.Exists(path))
            {
                var bak = path + ".bak";
                try
                {
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(path, bak);
                }
                catch { }
            }
            File.Move(tmp, path);
        }

        // ------------------------------------------------------------ history

        public static void Record(string appId, string name, string exe, string icon,
                                  DateTime startedUtc, DateTime stoppedUtc, string reason)
        {
            var s = Settings.Current;
            var entry = new HistoryEntry
            {
                Id = appId,
                Name = name,
                Exe = exe,
                Icon = icon,
                Started = startedUtc.ToString("o", CultureInfo.InvariantCulture),
                Stopped = stoppedUtc.ToString("o", CultureInfo.InvariantCulture),
                Seconds = (int)Math.Max(0, (stoppedUtc - startedUtc).TotalSeconds),
                Reason = reason
            };

            lock (Gate)
            {
                s.History.Insert(0, entry);
                if (s.History.Count > MaxHistory)
                    s.History.RemoveRange(MaxHistory, s.History.Count - MaxHistory);
            }
            SaveQuiet();
        }

        public static void TouchRecent(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return;
            var s = Settings.Current;
            lock (Gate)
            {
                s.Recent.RemoveAll(delegate(string id) { return id == appId; });
                s.Recent.Insert(0, appId);
                if (s.Recent.Count > MaxRecent)
                    s.Recent.RemoveRange(MaxRecent, s.Recent.Count - MaxRecent);
            }
            SaveQuiet();
        }

        public static bool ToggleFavorite(string appId)
        {
            var s = Settings.Current;
            bool now;
            lock (Gate)
            {
                if (s.Favorites.Contains(appId))
                {
                    s.Favorites.Remove(appId);
                    now = false;
                }
                else
                {
                    s.Favorites.Add(appId);
                    now = true;
                }
            }
            SaveQuiet();
            return now;
        }

        // ------------------------------------------------------------ helpers

        public static Dictionary<string, object> Dict(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static string String(Dictionary<string, object> d, string key, string fallback)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return fallback;
            var s = d[key] as string;
            return s ?? Convert.ToString(d[key], CultureInfo.InvariantCulture);
        }

        public static bool Bool(Dictionary<string, object> d, string key, bool fallback)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return fallback;
            var o = d[key];
            if (o is bool) return (bool)o;
            bool b;
            return bool.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), out b) ? b : fallback;
        }

        public static int Int(Dictionary<string, object> d, string key, int fallback)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return fallback;
            var o = d[key];
            try
            {
                if (o is int) return (int)o;
                if (o is long) return (int)(long)o;
                if (o is double) return (int)(double)o;
                int i;
                return int.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), out i) ? i : fallback;
            }
            catch { return fallback; }
        }
    }
}
