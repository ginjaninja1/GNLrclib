using GNLrclib.UI.Config;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GNLrclib.Services
{
    /// <summary>
    /// Decides whether a file falls inside the libraries/paths the user opted in on the config page.
    /// A path under a disabled entry is always excluded; otherwise it must be under an enabled entry.
    /// </summary>
    public static class LibraryPathScope
    {
        public static bool IsInScope(string itemPath, IEnumerable<LibraryPathFilterItem> filters)
        {
            if (string.IsNullOrEmpty(itemPath) || filters == null)
            {
                return false;
            }

            var rows = filters.Where(row => !string.IsNullOrEmpty(row.Path)).ToList();

            if (rows.Any(row => !row.Enabled && IsUnderPath(itemPath, row.Path)))
            {
                return false;
            }

            return rows.Any(row => row.Enabled && IsUnderPath(itemPath, row.Path));
        }

        private static bool IsUnderPath(string itemPath, string root)
        {
            var normalizedRoot = root.TrimEnd('\\', '/');

            if (!itemPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Require a path boundary so that "D:\Music" does not match "D:\Music Videos".
            return itemPath.Length == normalizedRoot.Length
                || itemPath[normalizedRoot.Length] == '\\'
                || itemPath[normalizedRoot.Length] == '/';
        }
    }
}
