// The optional update check.
//
// One request, when the user has left it enabled, to a single address. It
// asks for a release list, reads the newest published version number, and
// pushes a result the interface turns into a prompt. Nothing about the
// machine is sent: no id, no account, no session data, and the reply is
// discarded after the version number is read.
//
// The check is off unless the settings file enables it, it never repeats in
// a session, and a failure is silent apart from a line in the log.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Zycord
{
    internal static class Updates
    {
        /// <summary>
        /// The public release list for this repository, from the API host. The
        /// web address that serves the same list is HTML, not JSON, so it
        /// cannot be read here; this is the JSON one.
        /// </summary>
        public const string Endpoint = "https://api.github.com/repos/" + AppInfo.RepoSlug + "/releases?per_page=20";

        private const int TimeoutMs = 8000;
        private const int MaxBody = 256 * 1024;
        private static readonly object Gate = new object();
        private static Thread _worker;

        /// <summary>True while a check is in flight.</summary>
        public static bool Busy { get { lock (Gate) { return _worker != null; } } }

        /// <summary>
        /// Start the startup check, if settings say to and one is not already
        /// running. Callers never wait: the window opens immediately and the
        /// result is pushed to the interface when it arrives.
        /// </summary>
        public static void Start()
        {
            if (!Store.UpdateCheckEnabled) return;
            Begin();
        }

        /// <summary>
        /// Run a check the user asked for. It ignores the startup setting,
        /// because asking is an instruction to check now, but it still runs
        /// at most once at a time and only ever contacts the fixed endpoint.
        /// </summary>
        public static void StartManual()
        {
            Begin();
        }

        private static void Begin()
        {
            lock (Gate)
            {
                if (_worker != null) return;
                var thread = new Thread(delegate() { Run(Endpoint); });
                thread.IsBackground = true;
                thread.Name = "Zycord update check";
                _worker = thread;
                thread.Start();
            }
        }

        private static void Run(string url)
        {
            string state;
            string version = null;
            string error = null;

            try
            {
                var body = Fetch(url);
                if (body == null)
                {
                    state = "error";
                    error = "The update server did not answer with a success status.";
                }
                else
                {
                    version = NewestVersion(body);
                    if (version == null)
                    {
                        state = "error";
                        error = "The reply did not carry a version number.";
                    }
                    else if (AppInfo.IsNewer(version))
                    {
                        state = "available";
                    }
                    else
                    {
                        state = "current";
                    }
                }
            }
            catch (WebException ex)
            {
                state = "error";
                error = Describe(ex);
            }
            catch (Exception ex)
            {
                state = "error";
                error = ex.Message;
            }
            finally
            {
                lock (Gate) { _worker = null; }
            }

            if (state != "available")
            {
                Log.Info("update check: " + state + (error == null ? "" : " (" + error + ")"));
            }
            else
            {
                Log.Info("update check: " + AppInfo.Version + " installed, " + version + " available");
            }

            var payload = new Dictionary<string, object>
            {
                { "state", state },
                { "current", AppInfo.Version }
            };
            if (version != null)
            {
                payload["version"] = version;
                payload["download"] = AppInfo.DownloadUrl(version);
                payload["page"] = AppInfo.Repo + "/releases/tag/v" + version;
            }
            if (error != null) payload["error"] = error;
            payload["canApply"] = CanApply;

            Bridge.PushEvent("update", payload);
        }

        // ------------------------------------------------------- applying an update

        /// <summary>
        /// True when this copy may replace itself: it has to be the installed
        /// build, because a portable single file is replaced by the user
        /// downloading a new one, not by overwriting a file it may still need.
        /// </summary>
        public static bool CanApply
        {
            get { return Setup.IsRunningInstalled; }
        }

        /// <summary>Where a downloaded and verified release is unpacked.</summary>
        public static string StagedDirectory()
        {
            return Path.Combine(Path.Combine(Paths.Data, "update"), "staged");
        }

        private static string PendingPath()
        {
            return Path.Combine(Paths.Data, "update", "pending.txt");
        }

        /// <summary>True when a verified release is unpacked and waiting to be applied.</summary>
        public static bool HasPending
        {
            get
            {
                try
                {
                    return File.Exists(PendingPath()) && Directory.Exists(StagedDirectory());
                }
                catch { return false; }
            }
        }

        public static void ClearPending()
        {
            try { if (File.Exists(PendingPath())) File.Delete(PendingPath()); } catch { }
            try { if (Directory.Exists(StagedDirectory())) Directory.Delete(StagedDirectory(), true); } catch { }
        }

        /// <summary>
        /// Download the release archive for <paramref name="version"/>, check it
        /// against the SHA-256 the release publishes, and unpack the inner
        /// folder into the staging directory. Progress is pushed as "download"
        /// events so the interface can show it.
        /// </summary>
        public static void BeginDownload(string version)
        {
            lock (Gate)
            {
                if (_worker != null) return;
                var thread = new Thread(delegate() { Download(version); });
                thread.IsBackground = true;
                thread.Name = "Zycord update download";
                _worker = thread;
                thread.Start();
            }
        }

        private static void Download(string version)
        {
            try
            {
                if (!CanApply) { Fail("This copy cannot update itself. Download the new version instead."); return; }

                Progress("download", 0, "Contacting the release page");
                var sha = PublishedChecksum(version);
                if (sha == null)
                {
                    Fail("The release does not publish a checksum, so it was not installed.");
                    return;
                }

                var archive = Path.Combine(Path.GetTempPath(), "zycord-" + version + ".zip");
                Progress("download", 5, "Downloading " + version);
                DownloadArchive(AppInfo.DownloadUrl(version), archive);

                Progress("download", 70, "Checking the download");
                var actual = Sha256(archive);
                if (!string.Equals(actual, sha, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(archive); } catch { }
                    Fail("The download did not match its published checksum, so it was discarded.");
                    return;
                }

                Progress("download", 80, "Unpacking");
                ExtractInner(archive);

                try { File.Delete(archive); } catch { }
                File.WriteAllText(PendingPath(), version, new UTF8Encoding(false));

                Log.Info("update " + version + " staged, checksum " + sha);
                Progress("staged", 100, "Ready to install " + version);
            }
            catch (Exception ex)
            {
                Log.Error("update download failed: " + ex);
                Fail(ex.Message);
            }
            finally
            {
                lock (Gate) { _worker = null; }
            }
        }

        private static void Fail(string message)
        {
            var payload = new Dictionary<string, object>
            {
                { "state", "error" },
                { "error", message },
                { "current", AppInfo.Version }
            };
            Bridge.PushEvent("update", payload);
        }

        private static void Progress(string state, int percent, string message)
        {
            Bridge.PushEvent("update", new Dictionary<string, object>
            {
                { "state", state },
                { "percent", percent },
                { "message", message },
                { "current", AppInfo.Version }
            });
        }

        /// <summary>The archive's SHA-256, from the text the release publishes beside it.</summary>
        private static string PublishedChecksum(string version)
        {
            try
            {
                var text = FetchText(AppInfo.Repo + "/releases/download/v" + version + "/SHA256.txt");
                if (text == null) return null;

                // The file is "<hex>  Zycord-<version>.zip"; the first run of
                // sixty-four hex characters is the digest.
                var match = System.Text.RegularExpressions.Regex.Match(text, "\\b[0-9a-fA-F]{64}\\b");
                return match.Success ? match.Value.ToLowerInvariant() : null;
            }
            catch (Exception ex)
            {
                Log.Warn("update: checksum fetch failed: " + ex.Message);
                return null;
            }
        }

        private static void DownloadArchive(string url, string destination)
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { }

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "Zycord/" + AppInfo.Version;
            request.Timeout = 30000;
            request.ReadWriteTimeout = 30000;
            request.AllowAutoRedirect = true;

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var input = response.GetResponseStream())
            using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write))
            {
                var total = response.ContentLength;
                var buffer = new byte[64 * 1024];
                long done = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    done += read;
                    if (total > 0)
                        Progress("download", 5 + (int)(60L * done / total), "Downloading");
                }
            }
        }

        private static string Sha256(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(stream);
                var text = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) text.Append(b.ToString("x2"));
                return text.ToString();
            }
        }

        /// <summary>
        /// The archive holds one top-level folder. Its contents, not the folder,
        /// are what replace the installed files.
        /// </summary>
        private static void ExtractInner(string archive)
        {
            var work = Path.Combine(Paths.Data, "update", "extract");
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);

            System.IO.Compression.ZipFile.ExtractToDirectory(archive, work);
            var roots = Directory.GetDirectories(work);
            var source = roots.Length == 1 ? roots[0] : work;

            var staged = StagedDirectory();
            if (Directory.Exists(staged)) Directory.Delete(staged, true);
            Directory.CreateDirectory(staged);

            foreach (var file in new DirectoryInfo(source).GetFiles("*", SearchOption.AllDirectories))
            {
                var relative = file.FullName.Substring(source.Length).TrimStart('\\', '/');
                var target = Path.Combine(staged, relative);
                var directory = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.Copy(file.FullName, target, true);
            }

            try { Directory.Delete(work, true); } catch { }
        }

        /// <summary>The response body, or null when the status is not a success.</summary>
        private static string Fetch(string url)
        {
            return FetchText(url);
        }

        private static string FetchText(string url)
        {
            // The in-box .NET 4 compiler predates the TLS 1.2 enum name, so
            // the value is cast rather than referenced.
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { }

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.UserAgent = "Zycord/" + AppInfo.Version;
            request.Accept = "application/vnd.github+json";
            request.Timeout = TimeoutMs;
            request.ReadWriteTimeout = TimeoutMs;
            request.AllowAutoRedirect = true;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if ((int)response.StatusCode < 200 || (int)response.StatusCode > 299)
                {
                    Log.Warn("update check: HTTP " + (int)response.StatusCode);
                    return null;
                }

                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    var buffer = new char[MaxBody];
                    var read = reader.ReadBlock(buffer, 0, buffer.Length);
                    return new string(buffer, 0, read);
                }
            }
        }

        /// <summary>
        /// The newest version number in a release reply. A single release or a
        /// list are both accepted. Drafts and prereleases are skipped: the
        /// check exists to offer a finished build, and a prerelease is not one.
        /// </summary>
        private static string NewestVersion(string body)
        {
            List<object> releases;
            try
            {
                var parsed = Store.Serialiser.DeserializeObject(body);
                var list = parsed as object[];
                if (list != null)
                {
                    releases = new List<object>(list);
                }
                else
                {
                    var single = parsed as Dictionary<string, object>;
                    if (single == null) return null;
                    releases = new List<object> { single };
                }
            }
            catch (Exception ex)
            {
                Log.Warn("update check: reply did not parse: " + ex.Message);
                return null;
            }

            string best = null;
            foreach (var entry in releases)
            {
                var release = entry as Dictionary<string, object>;
                if (release == null) continue;
                if (Store.Bool(release, "draft", false)) continue;
                if (Store.Bool(release, "prerelease", false)) continue;

                var version = CleanVersion(Store.String(release, "tag_name", null));
                if (version == null) continue;
                if (best == null || AppInfo.IsNewerThan(version, best)) best = version;
            }

            return best;
        }

        /// <summary>"v1.2.0", "1.2.0" and "release-1.2.0" all yield "1.2.0".</summary>
        private static string CleanVersion(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            var value = tag.Trim();
            var start = 0;
            while (start < value.Length && !char.IsDigit(value[start])) start++;
            var end = start;
            while (end < value.Length && (char.IsDigit(value[end]) || value[end] == '.')) end++;
            if (end <= start) return null;

            var version = value.Substring(start, end - start).TrimEnd('.');
            return version.Length == 0 ? null : version;
        }

        /// <summary>A short reason a check could not complete, for the log.</summary>
        private static string Describe(WebException ex)
        {
            if (ex.Status == WebExceptionStatus.Timeout) return "the request timed out";
            if (ex.Status == WebExceptionStatus.NameResolutionFailure) return "the host could not be resolved";
            if (ex.Status == WebExceptionStatus.ConnectFailure) return "the connection failed";

            var response = ex.Response as HttpWebResponse;
            if (response != null) return "HTTP " + (int)response.StatusCode;
            return ex.Message;
        }
    }
}
