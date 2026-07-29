using System;
using System.Globalization;

namespace Lorestead.Core.Sync
{
    public static class Timestamps
    {
        // "O" keeps full precision and a fixed layout, so UTC timestamps sort correctly
        // as plain strings in both SQLite (BINARY) and C# (ordinal).
        public static string UtcNowIso()
        {
            return DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        }
    }
}
