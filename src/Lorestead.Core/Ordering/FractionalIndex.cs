using System;

namespace Lorestead.Core.Ordering
{
    // Sortable string keys (Figma/Linear style): Between(a, b) yields a key strictly
    // between its bounds, so a reorder touches exactly one row (decisions.md). The
    // alphabet is ASCII-ordered, so SQLite BINARY collation and C# ordinal comparison
    // agree with alphabet order. Keys never end in '0' (a trailing '0' would leave no
    // room to generate a key immediately before it).
    public static class FractionalIndex
    {
        private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        private const char MidDigit = 'V';

        public static string Between(string a, string b)
        {
            Validate(a, nameof(a));
            Validate(b, nameof(b));
            if (a != null && b != null && string.CompareOrdinal(a, b) >= 0)
            {
                throw new ArgumentException($"Lower bound '{a}' must sort before upper bound '{b}'.");
            }

            string result;
            if (a == null && b == null)
            {
                result = MidDigit.ToString();
            }
            else if (b == null)
            {
                result = KeyAfter(a);
            }
            else
            {
                result = Midpoint(a ?? string.Empty, b);
            }
            return result;
        }

        private static string Midpoint(string a, string b)
        {
            string result = null;
            int i = 0;

            while (result == null)
            {
                int digitA = i < a.Length ? DigitIndex(a[i]) : 0;
                int digitB = DigitIndex(b[i]);

                if (digitA == digitB)
                {
                    i++;
                }
                else if (digitB - digitA >= 2)
                {
                    result = b.Substring(0, i) + Alphabet[(digitA + digitB) / 2];
                }
                else
                {
                    // Adjacent digits: fix the lower digit, then anything after a's
                    // remainder works - the differing digit already keeps it below b.
                    string aRest = i + 1 <= a.Length ? a.Substring(i + 1) : string.Empty;
                    result = b.Substring(0, i) + Alphabet[digitA] + KeyAfter(aRest);
                }
            }
            return result;
        }

        // Shortest reasonable key strictly greater than s (no upper bound): bump the first
        // non-'z' digit and truncate; all-'z' (or empty) appends '1' - the lowest legal
        // digit - so sequential appends burn through all 61 values per added character.
        private static string KeyAfter(string s)
        {
            string result = null;
            for (int i = 0; i < s.Length && result == null; i++)
            {
                int digit = DigitIndex(s[i]);
                if (digit < Alphabet.Length - 1)
                {
                    result = s.Substring(0, i) + Alphabet[digit + 1];
                }
            }
            if (result == null)
            {
                result = s + Alphabet[1];
            }
            return result;
        }

        private static int DigitIndex(char c)
        {
            int index = Alphabet.IndexOf(c);
            if (index < 0)
            {
                throw new ArgumentException($"Character '{c}' is not a valid fractional-index digit.");
            }
            return index;
        }

        private static void Validate(string key, string paramName)
        {
            if (key != null)
            {
                if (key.Length == 0)
                {
                    throw new ArgumentException("Fractional-index keys cannot be empty.", paramName);
                }
                if (key[key.Length - 1] == Alphabet[0])
                {
                    throw new ArgumentException($"Fractional-index key '{key}' must not end with '{Alphabet[0]}'.", paramName);
                }
                foreach (char c in key)
                {
                    DigitIndex(c);
                }
            }
        }
    }
}
