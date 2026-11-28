// Installation: turning the single executable into an installed copy.
//
// The build embeds the whole release folder as one zlib resource, so
// Zycord.exe stands alone. Running it from anywhere other than an installed
// location offers to install: it extracts the payload once into
// %LOCALAPPDATA%\Zycord\app, writes a Start Menu shortcut, records an entry in
// Add or remove programs, and starts the installed copy. After that every
// launch goes straight to the extracted files, which is what lets an update
// replace them.
//
// The data folder is chosen so that the single file and the installed copy
// share settings, logs and prepared games rather than splitting them.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Zycord
{
    internal static class Setup
    {
        private const string PayloadName = "Zycord.release.bin";

        /// <summary>
        /// Stamped immediately before the deflate stream. It fixes where the
        /// payload begins, which the verifier relies on; the reader skips it.
        /// </summary>
        private static readonly byte[] PayloadMagic =
            Encoding.ASCII.GetBytes("ZYCRDPAYLOAD1\0\0\0");

        /// <summary>Where an installed copy lives.</summary>
        public static string InstallDirectory
        {
            get { return Paths.App; }
        }

        public static bool IsInstalled
        {
            get
            {
                try { return File.Exists(Path.Combine(InstallDirectory, "Zycord.exe")); }
                catch { return false; }
            }
        }

        /// <summary>True when this process is the extracted copy, not the single file.</summary>
        public static bool IsRunningInstalled
        {
            get
            {
                try
                {
                    return string.Equals(
                        Path.GetFullPath(Paths.ExeDir),
                        Path.GetFullPath(InstallDirectory),
                        StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Called once at startup. If this executable carries the payload and
        /// is not already the installed copy, offer to install; a user who
        /// declines runs the portable copy in place, which still works.
        /// </summary>
        public static void OfferInstallIfNeeded(Action onDeclined)
        {
            if (IsRunningInstalled) return;
            if (!HasPayload()) { onDeclined(); return; }

            var answer = MessageBox.Show(
                "Install Zycord on this PC?\n\n" +
                "This copies the application to " + InstallDirectory + ", adds a Start Menu " +
                "shortcut, and lets Zycord update itself when a new version is released.\n\n" +
                "Choose No to run it once from this file without installing.",
                "Install Zycord", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

            if (answer == DialogResult.Cancel) { Environment.Exit(0); return; }
            if (answer != DialogResult.Yes) { onDeclined(); return; }

            string error;
            if (!Install(out error))
            {
                MessageBox.Show("Zycord could not install itself.\n\n" + error +
                                "\n\nIt will run from this file instead.",
                    "Zycord", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                onDeclined();
                return;
            }

            LaunchInstalled();
            Environment.Exit(0);
        }

        public static bool HasPayload()
        {
            try
            {
                return Assembly.GetExecutingAssembly().GetManifestResourceInfo(PayloadName) != null;
            }
            catch { return false; }
        }

        /// <summary>Extract the payload, then write the shortcut and registry entry.</summary>
        public static bool Install(out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(InstallDirectory);

                CheckPayload();
                WritePayload();

                CreateShortcut();
                WriteUninstallEntry();
                Log.Info("installed to " + InstallDirectory);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error("install failed: " + ex);
                return false;
            }
        }

        private static void CheckPayload()
        {
            if (!HasPayload()) throw new InvalidDataException("The release payload is missing from this file.");
        }

        /// <summary>
        /// The payload is a four-byte little-endian manifest length, the
        /// manifest (one "path\0length\n" line per file), then the concatenated
        /// file bytes in the same order. The length prefix is what keeps the
        /// binary half from being read as text.
        /// </summary>
        private static void WritePayload()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadName))
            {
                if (stream == null) throw new InvalidDataException("The release payload is missing.");
                // The marker sits in front of the deflate stream, so it has to be
                // read from the resource itself. Constructing the DeflateStream
                // first and reading the marker through it tries to inflate the
                // marker as compressed data, which fails with "invalid distance
                // too far back" on every install.
                var magic = ReadExactly(stream, PayloadMagic.Length);
                for (var i = 0; i < magic.Length; i++)
                {
                    if (magic[i] != PayloadMagic[i])
                        throw new InvalidDataException("The release payload is not in the expected format.");
                }

                using (var zlib = new DeflateStream(stream, CompressionMode.Decompress))
                {
                    var lengthBytes = ReadExactly(zlib, 4);
                    var manifestLength = lengthBytes[0] | (lengthBytes[1] << 8) |
                                         (lengthBytes[2] << 16) | (lengthBytes[3] << 24);
                    if (manifestLength <= 0 || manifestLength > 16 * 1024 * 1024)
                        throw new InvalidDataException("The release payload is damaged.");

                    var manifest = Encoding.UTF8.GetString(ReadExactly(zlib, manifestLength));

                    var files = new List<KeyValuePair<string, int>>();
                    var index = 0;
                    while (index < manifest.Length)
                    {
                        var pathEnd = manifest.IndexOf('\0', index);
                        if (pathEnd < 0) break;
                        var lengthEnd = manifest.IndexOf('\n', pathEnd + 1);
                        if (lengthEnd < 0) break;

                        var relative = manifest.Substring(index, pathEnd - index);
                        int length;
                        if (!int.TryParse(manifest.Substring(pathEnd + 1, lengthEnd - pathEnd - 1), out length)
                            || length < 0)
                            throw new InvalidDataException("The release payload is damaged.");
                        files.Add(new KeyValuePair<string, int>(relative, length));
                        index = lengthEnd + 1;
                    }

                    var buffer = new byte[64 * 1024];
                    foreach (var file in files)
                    {
                        var target = SafeTarget(file.Key);
                        var directory = Path.GetDirectoryName(target);
                        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                        using (var output = new FileStream(target, FileMode.Create, FileAccess.Write))
                        {
                            var left = file.Value;
                            while (left > 0)
                            {
                                var got = zlib.Read(buffer, 0, Math.Min(buffer.Length, left));
                                if (got <= 0) throw new InvalidDataException("The release payload ended early.");
                                output.Write(buffer, 0, got);
                                left -= got;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Read exactly <paramref name="count"/> bytes. A single Read can return
        /// fewer than asked for, so a short read is not an error on its own.
        /// </summary>
        private static byte[] ReadExactly(Stream stream, int count)
        {
            var buffer = new byte[count];
            var read = 0;
            while (read < count)
            {
                var got = stream.Read(buffer, read, count - read);
                if (got <= 0) throw new InvalidDataException("The release payload ended early.");
                read += got;
            }
            return buffer;
        }

        /// <summary>
        /// A payload path is untrusted input in the sense that a corrupted
        /// archive could name anything. Every target must stay inside the
        /// install folder.
        /// </summary>
        private static string SafeTarget(string relative)
        {
            var clean = relative.Replace('/', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(InstallDirectory, clean));
            var root = Path.GetFullPath(InstallDirectory) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The release payload tried to write outside the install folder: " + relative);
            return target;
        }

        // ------------------------------------------------------------- shell integration

        public static void CreateShortcut()
        {
            var link = new List<string>(UpdateApply.ShortcutPaths());
            var startMenu = link[0];
            var exe = Path.Combine(InstallDirectory, "Zycord.exe");

            var directory = Path.GetDirectoryName(startMenu);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // A .lnk is a shell object, created through WScript.Shell, which
            // every supported Windows carries. No COM interop assembly is
            // referenced, so the build stays the plain csc one.
            dynamic shell = null;
            try
            {
                var type = Type.GetTypeFromProgID("WScript.Shell");
                if (type == null) return;
                shell = Activator.CreateInstance(type);
                dynamic shortcut = shell.CreateShortcut(startMenu);
                shortcut.TargetPath = exe;
                shortcut.WorkingDirectory = InstallDirectory;
                shortcut.IconLocation = exe + ",0";
                shortcut.Description = "Zycord";
                shortcut.Save();
            }
            catch (Exception ex) { Log.Warn("install: shortcut: " + ex.Message); }
            finally
            {
                try { if (shell != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell); }
                catch { }
            }
        }

        public static void WriteUninstallEntry()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(UpdateApply.UninstallKey))
                {
                    if (key == null) return;
                    var exe = Path.Combine(InstallDirectory, "Zycord.exe");
                    key.SetValue("DisplayName", "Zycord");
                    key.SetValue("DisplayVersion", AppInfo.Version);
                    key.SetValue("DisplayIcon", exe + ",0");
                    key.SetValue("Publisher", "Zycord");
                    key.SetValue("InstallLocation", InstallDirectory);
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    key.SetValue("UninstallString", "\"" + exe + "\" --uninstall");
                    key.SetValue("QuietUninstallString", "\"" + exe + "\" --uninstall --silent");
                    key.SetValue("EstimatedSize", EstimateKb(), RegistryValueKind.DWord);
                }
            }
            catch (Exception ex) { Log.Warn("install: registry: " + ex.Message); }
        }

        private static int EstimateKb()
        {
            try
            {
                long total = 0;
                foreach (var file in new DirectoryInfo(InstallDirectory).GetFiles("*", SearchOption.AllDirectories))
                    total += file.Length;
                return (int)Math.Min(int.MaxValue, total / 1024);
            }
            catch { return 0; }
        }

        private static void LaunchInstalled()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Path.Combine(InstallDirectory, "Zycord.exe"))
                {
                    UseShellExecute = true,
                    WorkingDirectory = InstallDirectory
                });
            }
            catch (Exception ex) { Log.Error("install: launch failed: " + ex.Message); }
        }

        /// <summary>True when this build was made for installation (it carries a payload).</summary>
        public static bool SupportsInstall { get { return HasPayload(); } }
    }
}
