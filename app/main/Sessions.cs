// Starting and stopping the dummy game processes.
//
// A session is a copy of the bundled stub renamed to the executable Discord
// expects for a given game. Discord sees that process running and treats the
// game as launched. Every session is tracked by process id, never by image
// name, so stopping one can never terminate an unrelated program that happens
// to share the executable name.
//
// Two safety rules apply throughout:
//   * a path is only written after it has been validated and re-checked as
//     sitting inside the games folder;
//   * a process is only terminated after confirming it still points at the
//     file this class created for it.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Zycord
{
    internal sealed class Session
    {
        public string AppId { get; set; }
        public string Name { get; set; }
        public string Icon { get; set; }
        public string ExePath { get; set; }
        public string RelativeExe { get; set; }
        public int Pid { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime? DeadlineUtc { get; set; }
        public bool HistoryRecorded { get; set; }

        public bool Running { get { return Sessions.IsAlive(Pid); } }

        public int Seconds
        {
            get { return Running ? (int)Math.Max(0, (DateTime.UtcNow - StartedUtc).TotalSeconds) : 0; }
        }

        public Dictionary<string, object> ToJson()
        {
            var running = Running;
            var d = new Dictionary<string, object>
            {
                { "appId", AppId },
                { "name", Name },
                { "icon", Icon ?? "" },
                { "exe", RelativeExe ?? "" },
                { "pid", Pid },
                { "running", running },
                { "startedUtc", StartedUtc.ToString("o") },
                { "seconds", running ? (int)Math.Max(0, (DateTime.UtcNow - StartedUtc).TotalSeconds) : 0 }
            };
            if (DeadlineUtc.HasValue)
            {
                d["remaining"] = Math.Max(0,
                    (int)(DeadlineUtc.Value - DateTime.UtcNow).TotalSeconds);
            }
            return d;
        }
    }

    internal static class Sessions
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Session> Active =
            new Dictionary<string, Session>(StringComparer.Ordinal);

        private static readonly Regex SafeAppId = new Regex("^[0-9]{1,25}$", RegexOptions.Compiled);

        // The display name only reaches the session window title and the stub's
        // --title argument. It never becomes part of a file path, so any
        // printable character is allowed; only control characters are refused.
        private static readonly Regex UnsafeName =
            new Regex("[\\x00-\\x1F\\x7F]", RegexOptions.Compiled);

        public static bool IsAlive(int pid)
        {
            if (pid <= 0) return false;
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    return p != null && !p.HasExited;
                }
            }
            catch { return false; }
        }

        /// <summary>True when a path sits strictly inside a directory.</summary>
        public static bool IsInside(string root, string candidate)
        {
            try
            {
                var r = Path.GetFullPath(root)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                var c = Path.GetFullPath(candidate);
                return c.StartsWith(r, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// Reduce an executable entry from the catalog to a safe relative path.
        /// Returns null when the entry could escape the games directory.
        /// </summary>
        public static string SafeRelativeExe(string entry)
        {
            if (string.IsNullOrWhiteSpace(entry)) return null;

            var normalised = entry.Replace('\\', '/').Trim();
            if (normalised.StartsWith("/", StringComparison.Ordinal)) return null;
            if (normalised.IndexOf('\0') >= 0) return null;

            var parts = normalised.Split('/');
            var clean = new List<string>();
            foreach (var part in parts)
            {
                var p = part.Trim();
                if (p.Length == 0 || p == ".") continue;
                if (p == "..") return null;
                if (p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
                if (p.IndexOf(':') >= 0) return null;
                clean.Add(p);
            }

            if (clean.Count == 0) return null;
            var last = clean[clean.Count - 1];
            if (!last.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            if (clean.Count > 8) return null;

            return string.Join("/", clean.ToArray());
        }

        public static string StubPath
        {
            get { return Path.Combine(Paths.ExeDir, "stub.exe"); }
        }

        public static Dictionary<string, object> Start(string appId, string name, string exeEntry, string icon)
        {
            if (!SafeAppId.IsMatch(appId ?? ""))
                return Error("That application id is not valid.");
            if (string.IsNullOrWhiteSpace(name) || name.Length > 200 || UnsafeName.IsMatch(name))
                return Error("That game name cannot be used as a window title.");

            var relative = SafeRelativeExe(exeEntry);
            if (relative == null)
                return Error("That executable path cannot be used safely.");

            if (!File.Exists(StubPath))
                return Error("The Zycord session stub is missing from the installation folder.");

            lock (Gate)
            {
                Session existing;
                if (Active.TryGetValue(appId, out existing))
                {
                    if (existing.Running)
                    {
                        return Ok(new Dictionary<string, object>
                        {
                            { "session", existing.ToJson() },
                            { "already", true }
                        });
                    }
                    // The process died on its own. Record it, then start afresh.
                    Retire(existing, "exited");
                }

                var folder = Path.Combine(Paths.Games, appId);
                var target = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
                var targetDir = Path.GetDirectoryName(target);

                try
                {
                    Directory.CreateDirectory(targetDir);
                    if (!IsInside(Paths.Games, target))
                        return Error("That executable path resolves outside the Zycord games folder.");

                    File.Copy(StubPath, target, true);
                }
                catch (Exception ex)
                {
                    Log.Error("stub copy failed: " + ex.Message);
                    return Error("Zycord could not prepare the session file: " + ex.Message);
                }

                try
                {
                    var psi = new ProcessStartInfo(target)
                    {
                        UseShellExecute = false,
                        WorkingDirectory = targetDir,
                        CreateNoWindow = true,
                        Arguments = BuildArguments(name, appId)
                    };

                    var proc = Process.Start(psi);
                    if (proc == null) return Error("The session process did not start.");

                    var settings = Settings.Current;
                    var session = new Session
                    {
                        AppId = appId,
                        Name = name,
                        Icon = icon,
                        ExePath = target,
                        RelativeExe = relative,
                        Pid = proc.Id,
                        StartedUtc = DateTime.UtcNow
                    };

                    if (settings.AutoStopMinutes > 0)
                        session.DeadlineUtc = session.StartedUtc.AddMinutes(settings.AutoStopMinutes);

                    Active[appId] = session;
                    Store.TouchRecent(appId);
                    Log.Info("session started app=" + appId + " pid=" + proc.Id + " exe=" + relative);
                    return Ok(new Dictionary<string, object>
                    {
                        { "session", session.ToJson() },
                        { "already", false }
                    });
                }
                catch (Exception ex)
                {
                    Log.Error("session start failed: " + ex.Message);
                    return Error("The session could not start: " + ex.Message);
                }
            }
        }

        private static string BuildArguments(string name, string appId)
        {
            var safe = (name ?? "").Replace("\"", "'");
            var args = "--title \"" + safe + "\" --app-id " + appId +
                       " --parent " + Process.GetCurrentProcess().Id;
            if (!Settings.Current.ShowSessionWindow) args += " --hidden";
            return args;
        }

        public static Dictionary<string, object> Stop(string appId)
        {
            lock (Gate)
            {
                Session s;
                if (!Active.TryGetValue(appId, out s))
                {
                    return Ok(new Dictionary<string, object>
                    {
                        { "stopped", false },
                        { "reason", "not-running" }
                    });
                }

                var killed = Terminate(s);
                Retire(s, killed ? "stopped" : "exited");
                return Ok(new Dictionary<string, object>
                {
                    { "stopped", killed },
                    { "reason", killed ? "stopped" : "already-exited" }
                });
            }
        }

        /// <summary>
        /// Push a running session's automatic stop time further out, or set one
        /// when the session was started with no timer. Returns null when the
        /// session is not running.
        /// </summary>
        public static Dictionary<string, object> Extend(string appId, int minutes)
        {
            if (minutes < 1) minutes = 1;
            if (minutes > 240) minutes = 240;

            lock (Gate)
            {
                Session s;
                if (!Active.TryGetValue(appId ?? "", out s) || !s.Running) return null;

                var from = s.DeadlineUtc.HasValue && s.DeadlineUtc.Value > DateTime.UtcNow
                    ? s.DeadlineUtc.Value
                    : DateTime.UtcNow;
                s.DeadlineUtc = from.AddMinutes(minutes);

                Log.Info("session extended app=" + appId + " minutes=" + minutes);
                return Ok(new Dictionary<string, object>
                {
                    { "session", s.ToJson() },
                    { "minutes", minutes }
                });
            }
        }

        public static int StopAll()
        {
            lock (Gate)
            {
                var count = 0;
                foreach (var s in new List<Session>(Active.Values))
                {
                    var killed = Terminate(s);
                    if (killed) count++;
                    Retire(s, killed ? "stopped" : "exited");
                }
                Active.Clear();
                return count;
            }
        }

        /// <summary>
        /// Called once a second by the shell. Stops any session whose automatic
        /// stop timer has expired and reports it so the interface can update.
        /// </summary>
        public static List<string> Tick()
        {
            List<string> stopped = null;

            lock (Gate)
            {
                if (Active.Count == 0) return null;

                foreach (var s in new List<Session>(Active.Values))
                {
                    if (!s.Running)
                    {
                        Retire(s, "exited");
                        if (stopped == null) stopped = new List<string>();
                        stopped.Add(s.AppId);
                        continue;
                    }

                    if (s.DeadlineUtc.HasValue && DateTime.UtcNow >= s.DeadlineUtc.Value)
                    {
                        Log.Info("auto stop app=" + s.AppId + " pid=" + s.Pid);
                        Terminate(s);
                        Retire(s, "auto");
                        if (stopped == null) stopped = new List<string>();
                        stopped.Add(s.AppId);
                    }
                }
            }

            return stopped;
        }

        /// <summary>
        /// Terminate a session, but only after confirming the process still
        /// points at the executable Zycord created for it. If the pid has been
        /// reused by an unrelated program, nothing is killed.
        /// </summary>
        private static bool Terminate(Session s)
        {
            if (!IsAlive(s.Pid)) return false;

            try
            {
                using (var p = Process.GetProcessById(s.Pid))
                {
                    string actual = null;
                    try { actual = p.MainModule.FileName; }
                    catch { }

                    if (actual != null && !string.Equals(
                            Path.GetFullPath(actual), Path.GetFullPath(s.ExePath),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Warn("refusing to stop pid " + s.Pid + ": it is no longer the Zycord stub");
                        return false;
                    }

                    p.Kill();
                    p.WaitForExit(4000);
                    Log.Info("session stopped app=" + s.AppId + " pid=" + s.Pid);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("session stop failed app=" + s.AppId + ": " + ex.Message);
                return false;
            }
        }

        /// <summary>Remove a session from the table and write its history entry once.</summary>
        private static void Retire(Session s, string reason)
        {
            if (s.HistoryRecorded) return;
            s.HistoryRecorded = true;
            Active.Remove(s.AppId);
            try
            {
                Store.Record(s.AppId, s.Name, s.RelativeExe, s.Icon, s.StartedUtc, DateTime.UtcNow, reason);
            }
            catch (Exception ex) { Log.Warn("history record failed: " + ex.Message); }
        }

        public static List<Session> Snapshot()
        {
            lock (Gate)
            {
                var list = new List<Session>();
                var stale = new List<Session>();

                foreach (var s in Active.Values)
                {
                    if (s.Running) list.Add(s);
                    else stale.Add(s);
                }

                foreach (var s in stale) Retire(s, "exited");
                return list;
            }
        }

        /// <summary>Remove the prepared game folders. Sessions must be stopped first.</summary>
        public static Dictionary<string, object> Cleanup()
        {
            if (Snapshot().Count > 0)
                return Error("Stop the running sessions before clearing the game files.");

            var removed = 0;
            try
            {
                foreach (var dir in Directory.GetDirectories(Paths.Games))
                {
                    try
                    {
                        if (!IsInside(Paths.Games, dir)) continue;
                        Directory.Delete(dir, true);
                        removed++;
                    }
                    catch (Exception ex) { Log.Warn("cleanup skipped " + dir + ": " + ex.Message); }
                }
            }
            catch (Exception ex) { Log.Error("cleanup failed: " + ex.Message); }

            Log.Info("cleanup removed " + removed + " folders");
            return Ok(new Dictionary<string, object> { { "removed", removed } });
        }

        /// <summary>
        /// Delete folders left behind by a previous run. Called at startup,
        /// before any session exists, so it can never touch a file in use.
        /// </summary>
        public static void SweepOrphans()
        {
            try
            {
                var removed = 0;
                foreach (var dir in Directory.GetDirectories(Paths.Games))
                {
                    try
                    {
                        if (!IsInside(Paths.Games, dir)) continue;
                        Directory.Delete(dir, true);
                        removed++;
                    }
                    catch { }
                }
                if (removed > 0) Log.Info("startup sweep removed " + removed + " stale game folders");
            }
            catch (Exception ex) { Log.Warn("startup sweep failed: " + ex.Message); }
        }

        public static long DiskUse()
        {
            long total = 0;
            try
            {
                foreach (var f in Directory.GetFiles(Paths.Games, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return total;
        }

        public static int FolderCount()
        {
            try { return Directory.GetDirectories(Paths.Games).Length; }
            catch { return 0; }
        }

        private static Dictionary<string, object> Ok(Dictionary<string, object> extra)
        {
            var d = new Dictionary<string, object> { { "ok", true } };
            foreach (var kv in extra) d[kv.Key] = kv.Value;
            return d;
        }

        private static Dictionary<string, object> Error(string message)
        {
            return new Dictionary<string, object> { { "ok", false }, { "error", message } };
        }
    }
}
