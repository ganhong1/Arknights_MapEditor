#nullable enable
using System;
using System.IO;

namespace Arknights.MapEditor.Storage
{
    internal static class SafeProjectPath
    {
        internal static string Validate(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.IndexOfAny(new[] { ':', '\\', '\0', '*', '?', '"', '<', '>', '|' }) >= 0)
                throw new ArgumentException("Use a relative, forward-slash resource path.", nameof(path));
            foreach (var character in path)
                if (char.IsControl(character)) throw new ArgumentException("Control characters are forbidden in paths.", nameof(path));
            foreach (var part in path.Split('/'))
            {
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(" ", StringComparison.Ordinal) || part.EndsWith(".", StringComparison.Ordinal))
                    throw new ArgumentException("Empty, traversal and ambiguous path segments are forbidden.", nameof(path));
                var stem = part.Split('.')[0].ToUpperInvariant();
                if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" ||
                    (stem.Length == 4 && (stem.StartsWith("COM",StringComparison.Ordinal) || stem.StartsWith("LPT",StringComparison.Ordinal)) && stem[3] >= '1' && stem[3] <= '9'))
                    throw new ArgumentException("Device names are forbidden in project paths.", nameof(path));
            }
            return path;
        }

        internal static void RejectLinks(string path)
        {
            for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Project and recovery paths must not traverse links or reparse points.");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
            }
        }
    }
}
