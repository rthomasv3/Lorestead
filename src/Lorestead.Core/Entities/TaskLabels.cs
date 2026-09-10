using System;
using System.Collections.Generic;

namespace Lorestead.Core.Entities
{
    public static class TaskLabels
    {
        // Trim, drop blanks, and dedupe without regard to case while keeping the
        // first spelling seen - "Agent" and "agent" are one label, shown the way it
        // was typed first. Order is preserved: it is the order the chips render in.
        public static List<string> Normalize(IEnumerable<string> labels)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in labels ?? Array.Empty<string>())
            {
                string label = (raw ?? string.Empty).Trim();
                if (label.Length > 0 && seen.Add(label))
                {
                    result.Add(label);
                }
            }
            return result;
        }
    }
}
