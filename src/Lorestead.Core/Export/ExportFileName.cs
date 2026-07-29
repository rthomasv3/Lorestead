using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Lorestead.Core.Export
{
    public static class ExportFileName
    {
        public const string Fallback = "Untitled";

        // The intersection of what Windows, macOS and Linux accept, because the export
        // is meant to be portable. Titles themselves stay free-form - the front matter
        // carries the real one (features/export.md).
        private const string IllegalCharacters = "<>:\"/\\|?*";

        // Long enough that no realistic title is cut, short enough that a deep subtree
        // stays inside Windows' 260-character path limit.
        private const int MaxLength = 120;

        // Reserved by Windows as device names whatever the extension, and every one of
        // them is a perfectly ordinary note title.
        private static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        public static string Sanitize(string title)
        {
            string result = Clean(title);

            if (result.Length == 0)
            {
                result = Fallback;
            }
            else if (ReservedNames.Contains(result))
            {
                result = result + "_";
            }

            return result;
        }

        // Split on the last dot so an attachment keeps its extension while the stem is
        // sanitized like any other name.
        public static string SanitizeAttachment(string filename)
        {
            string name = filename ?? string.Empty;
            int dot = name.LastIndexOf('.');
            string stem = dot > 0 ? name.Substring(0, dot) : name;
            string extension = dot > 0 ? Clean(name.Substring(dot + 1)) : string.Empty;
            return Sanitize(stem) + (extension.Length > 0 ? "." + extension : string.Empty);
        }

        // `used` holds names already taken in one directory and gains the returned one.
        // For notes the extension is empty and the base name is what gets reserved, so
        // Parent.md and its sibling Parent/ folder count as one claim.
        public static string Unique(string baseName, string extension, HashSet<string> used)
        {
            string candidate = baseName + extension;
            int counter = 2;

            while (used.Contains(candidate))
            {
                candidate = baseName + " (" + counter.ToString(CultureInfo.InvariantCulture) + ")" + extension;
                counter++;
            }

            used.Add(candidate);
            return candidate;
        }

        private static string Clean(string value)
        {
            StringBuilder builder = new StringBuilder((value ?? string.Empty).Length);
            bool pendingSpace = false;

            foreach (char character in value ?? string.Empty)
            {
                if (char.IsWhiteSpace(character) || char.IsControl(character) || IllegalCharacters.IndexOf(character) >= 0)
                {
                    pendingSpace = builder.Length > 0;
                }
                else
                {
                    if (pendingSpace)
                    {
                        builder.Append(' ');
                        pendingSpace = false;
                    }
                    builder.Append(character);
                }
            }

            string result = builder.ToString();
            if (result.Length > MaxLength)
            {
                result = result.Substring(0, MaxLength);
            }

            // Windows silently drops trailing dots and spaces, so a name ending in one
            // would not round-trip.
            return result.TrimEnd('.', ' ');
        }
    }
}
