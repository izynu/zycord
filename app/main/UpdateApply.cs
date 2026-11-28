// Applying a staged update, and removing an installed copy.
//
// Both run in a process that is not the one being replaced. Windows will not
// let a running image be overwritten, so the running Zycord starts a second
// copy of itself with --apply-update <pid>, waits for the first to exit, and
// the second one copies the staged files into place and launches the result.
// Nothing is downloaded here; Updates.cs has already fetched and unpacked the
// archive into the data folder.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Zycord
{
    internal static class UpdateApply
    {
        private const int MaxWaitMs = 30000;

        /// <summary>
        /// Wait for the process that started this one, copy the staged release
        /// over this installation, then start the new build.
        /// </summary>
        public static int Apply(int parentPid)
        {
            try
            {
                WaitForExit(parentPid);

                var staged = Updates.StagedDirectory();
                if (staged == null || !Directory.Exists(staged))
                {
                    Log.Error("apply update: the staged folder is missing");
                    return 1;
                }

                var copied = CopyTree(staged, Paths.ExeDir);
                Log.Info("apply update: copied " + copied + " files from " + staged);

                try { Directory.Delete(staged, true); } catch { }
                Updates.ClearPending();

                LaunchSelf();
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("apply update failed: " + ex);
                MessageBox.Show("Zycord could not finish updating.\n\n" + ex.Message +
                                "\n\nThe previous version is still installed.",
                    "Zycord", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                LaunchSelf();
                return 1;
            }
        }

        private static void WaitForExit(int pid)
        {
            try
            {
                using (var process = Process.GetProcessById(pid))
                {
                    if (!process.WaitForExit(MaxWaitMs))
                        Log.Warn("apply update: the previous process did not exit in time");
                }
            }
            catch (ArgumentException)
            {
                // Already gone, which is the normal case.
            }
        }

        /// <summary>Copy every file under <paramref name="from"/> into <paramref name="to"/>.</summary>
        private static int CopyTree(string from, string to)
        {
            var copied = 0;
            var root = new DirectoryInfo(from);
            foreach (var file in root.GetFiles("*", SearchOption.AllDirectories))
            {
                var relative = file.FullName.Substring(root.FullName.Length).TrimStart('\\', '/');
                var target = Path.Combine(to, relative);

                var directory = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                try
                {
                    if (File.Exists(target))
                    {
                        var attributes = File.GetAttributes(target);
                        if ((attributes & FileAttributes.ReadOnly) != 0)
                            File.SetAttributes(target, attributes & ~FileAttributes.ReadOnly);
                    }
                    File.Copy(file.FullName, target, true);
                    copied++;
                }
                catch (Exception ex)
                {
                    // A locked file is reported rather than aborting the whole
                    // update: the remaining files are still worth replacing,
                    // and the log says exactly what did not move.
                    Log.Warn("apply update: could not replace " + relative + ": " + ex.Message);
                }
            }
            return copied;
        }

        private static void LaunchSelf()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Paths.ExeDir
                });
            }
            catch (Exception ex) { Log.Error("apply update: relaunch failed: " + ex.Message); }
        }

        // ------------------------------------------------------------ uninstall

        /// <summary>
        /// Remove the installed copy: shortcuts, the uninstall entry, the data
        /// folder and the program files. The running executable cannot delete
        /// itself, so it is copied to TEMP and that copy does the deleting.
        /// </summary>
        public static int Uninstall(bool silent)
        {
            if (!silent)
            {
                var answer = MessageBox.Show(
                    "Remove Zycord from this computer?\n\n" +
                    "This deletes the program files, the Start Menu shortcut and everything " +
                    "in %LOCALAPPDATA%\\Zycord, including settings, logs and prepared game files.",
                    "Uninstall Zycord", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return 0;
            }

            var install = Paths.ExeDir;
            var copy = Path.Combine(Path.GetTempPath(), "zycord-uninstall-" + DateTime.Now.Ticks + ".exe");
            try
            {
                File.Copy(Application.ExecutablePath, copy, true);
                Process.Start(new ProcessStartInfo(copy, "\"" + install + "\"")
                {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("uninstall failed: " + ex);
                if (!silent)
                    MessageBox.Show("Zycord could not start the uninstaller.\n\n" + ex.Message +
                                    "\n\nDelete the Zycord folder yourself to finish removing it.",
                        "Zycord", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
        }

        /// <summary>
        /// Run by the TEMP copy: the original process has exited, so every file
        /// is free. Deletes the install folder, the shortcuts, the registry
        /// entry and the data folder.
        /// </summary>
        public static int UninstallFinish(string installDir)
        {
            var failures = new List<string>();

            Try(failures, "shortcut", delegate { RemoveShortcut(); });
            Try(failures, "registry", delegate { RemoveUninstallEntry(); });
            Try(failures, "data", delegate
            {
                if (Directory.Exists(Paths.Data)) Directory.Delete(Paths.Data, true);
            });
            Try(failures, "program files", delegate
            {
                if (Directory.Exists(installDir)) Directory.Delete(installDir, true);
            });

            Log.Info("uninstall finished, failures: " + failures.Count);
            if (failures.Count > 0)
            {
                try
                {
                    MessageBox.Show("Zycord was removed, but these items remain:\n\n" +
                                    string.Join("\n", failures.ToArray()),
                        "Zycord", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch { }
            }

            try
            {
                // The TEMP copy cannot delete itself while running; scheduling
                // it for deletion at the next restart keeps the folder clean.
                MoveFileEx(Application.ExecutablePath, null, 0x4 /* DELAY_UNTIL_REBOOT */);
            }
            catch { }

            return 0;
        }

        private static void Try(List<string> failures, string what, Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                failures.Add(what + ": " + ex.Message);
                Log.Warn("uninstall: " + what + " failed: " + ex.Message);
            }
        }

        public static void RemoveShortcut()
        {
            foreach (var path in ShortcutPaths())
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>The Start Menu and Desktop shortcuts an install creates.</summary>
        public static IEnumerable<string> ShortcutPaths()
        {
            var startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs");
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return new[] { Path.Combine(startMenu, "Zycord.lnk"), Path.Combine(desktop, "Zycord.lnk") };
        }

        public static void RemoveUninstallEntry()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            }
            catch (Exception ex) { Log.Warn("uninstall: registry: " + ex.Message); }
        }

        public const string UninstallKey =
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Zycord";

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool MoveFileEx(string existing, string replacement, int flags);
    }
}
