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

            Bridge.PushEvent("update", payload);
        }

        /// <summary>The response body, or null when the status is not a success.</summary>
        private static string Fetch(string url)
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
