using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SoulPlayer.Library
{
    internal static class SoulPath
    {
        internal static bool IsWindows
        {
            get { return Path.DirectorySeparatorChar == '\\'; }
        }

        internal static StringComparison Comparison
        {
            get { return ComparisonForPlatform(IsWindows); }
        }

        internal static StringComparer Comparer
        {
            get { return IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal; }
        }

        internal static string NormalizeConfiguredPath(string value, string baseDirectory)
        {
            return NormalizeForPlatform(value, baseDirectory, IsWindows);
        }

        internal static string NormalizeForPlatform(
            string value,
            string baseDirectory,
            bool windows)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            try
            {
                string cleaned = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
                cleaned = windows ? cleaned.Replace('/', '\\') : cleaned.Replace('\\', '/');

                if (!windows && Path.DirectorySeparatorChar == '\\')
                {
                    return NormalizeUnixForTests(cleaned, baseDirectory);
                }

                string resolved = Path.IsPathRooted(cleaned)
                    ? cleaned
                    : Path.Combine(baseDirectory ?? string.Empty, cleaned);
                string full = Path.GetFullPath(resolved);
                return TrimTrailingSeparatorsPreservingRoot(full);
            }
            catch
            {
                return string.Empty;
            }
        }

        internal static bool AreEquivalent(string left, string right)
        {
            string first = NormalizeConfiguredPath(left, string.Empty);
            string second = NormalizeConfiguredPath(right, string.Empty);
            return !string.IsNullOrEmpty(first) &&
                   string.Equals(first, second, Comparison);
        }

        internal static bool IsInside(string path, string root)
        {
            string fullPath = NormalizeConfiguredPath(path, string.Empty);
            string fullRoot = NormalizeConfiguredPath(root, string.Empty);
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(fullRoot))
            {
                return false;
            }

            if (string.Equals(fullPath, fullRoot, Comparison))
            {
                return true;
            }

            string prefix = fullRoot + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(prefix, Comparison);
        }

        internal static string CreateStablePathIdentity(string path)
        {
            return CreateStablePathIdentityForPlatform(
                path,
                string.Empty,
                IsWindows);
        }

        internal static string CreateStablePathIdentityForPlatform(
            string path,
            string baseDirectory,
            bool windows)
        {
            string normalized = NormalizeForPlatform(path, baseDirectory, windows);
            if (string.IsNullOrEmpty(normalized))
            {
                return string.Empty;
            }

            return "path:" + (windows ? normalized.ToUpperInvariant() : normalized);
        }

        internal static StringComparison ComparisonForPlatform(bool windows)
        {
            return windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }

        internal static string ToFileUri(string path)
        {
            string normalized = NormalizeConfiguredPath(path, string.Empty);
            return string.IsNullOrEmpty(normalized) ? string.Empty : new Uri(normalized).AbsoluteUri;
        }

        private static string TrimTrailingSeparatorsPreservingRoot(string path)
        {
            string root = Path.GetPathRoot(path) ?? string.Empty;
            string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed.Length < root.Length ? root : trimmed;
        }

        private static string NormalizeUnixForTests(string value, string baseDirectory)
        {
            string candidate = value.StartsWith("/", StringComparison.Ordinal)
                ? value
                : ((baseDirectory ?? string.Empty).Replace('\\', '/').TrimEnd('/') + "/" + value);
            bool rooted = candidate.StartsWith("/", StringComparison.Ordinal);
            List<string> parts = new List<string>();
            foreach (string part in candidate.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == ".")
                {
                    continue;
                }
                if (part == "..")
                {
                    if (parts.Count > 0)
                    {
                        parts.RemoveAt(parts.Count - 1);
                    }
                    continue;
                }
                parts.Add(part);
            }
            string result = string.Join("/", parts.ToArray());
            return rooted ? "/" + result : result;
        }
    }
}
