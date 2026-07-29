using System;
using System.Collections.Generic;

namespace Lorestead.Core.Import
{
    // Reads the YAML subset the three sources actually write: flat "key: value"
    // lines between --- fences. Block values (Joplin's tags list) belong to the
    // preceding key and are swallowed with it; anything fancier is not front matter
    // any exporter in scope produces, so a malformed block is treated as body.
    public static class ImportFrontMatter
    {
        public sealed class Result
        {
            public bool Present { get; set; }

            public string Title { get; set; }

            public string Created { get; set; }

            public string Updated { get; set; }

            public string LoresteadId { get; set; }

            public List<string> UnknownKeys { get; set; } = new List<string>();

            public string Body { get; set; }
        }

        public static Result Parse(string content)
        {
            // Bodies are stored with \n; normalizing here also keeps the
            // identical-content merge check from tripping on a CRLF re-save.
            string text = (content ?? string.Empty).Replace("\r\n", "\n");
            Result result = new Result { Body = text };

            if (text.StartsWith("---", StringComparison.Ordinal))
            {
                string[] lines = text.Split('\n');
                if (lines.Length > 0 && lines[0].TrimEnd('\r').Trim() == "---")
                {
                    int close = -1;
                    for (int index = 1; index < lines.Length && close < 0; index++)
                    {
                        string line = lines[index].TrimEnd('\r');
                        if (line.Trim() == "---")
                        {
                            close = index;
                        }
                    }

                    if (close > 0)
                    {
                        result.Present = true;
                        for (int index = 1; index < close; index++)
                        {
                            ReadLine(lines[index].TrimEnd('\r'), result);
                        }

                        int bodyStart = close + 1;
                        // Export writes one blank line after the fence; eat exactly
                        // one so a body that starts with intentional blanks keeps them.
                        if (bodyStart < lines.Length && lines[bodyStart].TrimEnd('\r').Length == 0)
                        {
                            bodyStart++;
                        }
                        result.Body = bodyStart >= lines.Length
                            ? string.Empty
                            : string.Join("\n", lines, bodyStart, lines.Length - bodyStart);
                    }
                }
            }

            return result;
        }

        private static void ReadLine(string line, Result result)
        {
            // Indented lines and list items continue the previous key's block value.
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && !line.StartsWith("-", StringComparison.Ordinal))
            {
                int colon = line.IndexOf(':');
                if (colon > 0)
                {
                    string key = line.Substring(0, colon).Trim();
                    string value = Unquote(line.Substring(colon + 1).Trim());

                    if (string.Equals(key, "title", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Title = value;
                    }
                    else if (string.Equals(key, "created", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Created = value;
                    }
                    else if (string.Equals(key, "updated", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Updated = value;
                    }
                    else if (string.Equals(key, "lorestead-id", StringComparison.OrdinalIgnoreCase))
                    {
                        result.LoresteadId = value;
                    }
                    else
                    {
                        result.UnknownKeys.Add(key);
                    }
                }
            }
        }

        // The inverse of the export's YamlScalar quoting, plus single quotes because
        // other tools write them.
        private static string Unquote(string value)
        {
            string result = value;

            if (result.Length >= 2 && result[0] == '"' && result[result.Length - 1] == '"')
            {
                result = result.Substring(1, result.Length - 2)
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\");
            }
            else if (result.Length >= 2 && result[0] == '\'' && result[result.Length - 1] == '\'')
            {
                result = result.Substring(1, result.Length - 2).Replace("''", "'");
            }

            return result;
        }
    }
}
