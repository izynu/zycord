// Locating the interface files that ship beside the launcher.

using System;
using System.IO;

namespace Zycord
{
    internal static class Ui
    {
        private static string _root;

        /// <summary>
        /// The folder holding index.html and its assets. A release puts it in
        /// "ui" beside the executable. During development the executable sits
        /// in dist/ inside the repository, so the source folder is checked too,
        /// which keeps a rebuild from being the only way to see a change.
        /// </summary>
        public static string Root
        {
            get
            {
                if (_root != null) return _root;

                var candidates = new[]
                {
                    Path.Combine(Paths.ExeDir, "ui"),
                    Path.Combine(Paths.ExeDir, "..", "app", "renderer"),
                    Path.Combine(Paths.ExeDir, "..", "..", "app", "renderer")
                };

                foreach (var c in candidates)
                {
                    try
                    {
                        var full = Path.GetFullPath(c);
                        if (File.Exists(Path.Combine(full, "index.html")))
                        {
                            _root = full;
                            return _root;
                        }
                    }
                    catch { }
                }

                throw new DirectoryNotFoundException(
                    "The Zycord interface files were not found. Expected a folder named 'ui' beside the executable.");
            }
        }

        /// <summary>True when the interface folder is the source tree, not a build.</summary>
        public static bool IsDevelopment
        {
            get
            {
                try
                {
                    return !string.Equals(
                        Path.GetFullPath(Root),
                        Path.GetFullPath(Path.Combine(Paths.ExeDir, "ui")),
                        StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }
        }
    }
}
