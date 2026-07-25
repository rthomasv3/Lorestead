using System;
using System.IO;

namespace SylvaNote.Core.DataAccess;

// Resolves the client-local data directory so the desktop client and the stdio
// MCP binary always open the SAME database, no matter which process starts first
// or which host spawned it. The profile root is used rather than %LOCALAPPDATA%
// because MSIX-packaged agent hosts virtualize AppData for the processes they
// spawn - a spawned binary silently forks a shadow copy of the DB - while leaving
// the profile root untouched (decisions.md). Both processes derive the same path
// independently, so there is no runtime handoff to break when the MCP runs first.
public static class LocalDataPaths
{
    private const string OverrideVariable = "SYLVANOTE_DATA_DIR";
    private const string DirectoryName = ".sylvanote";
    private const string LegacyDirectoryName = "SylvaNote";
    private const string DatabaseFileName = "sylvanote.db";

    public static string ResolveDataDirectory()
    {
        string overridden = Environment.GetEnvironmentVariable(OverrideVariable);
        string resolved;

        if (string.IsNullOrWhiteSpace(overridden))
        {
            resolved = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), DirectoryName);
        }
        else
        {
            resolved = overridden;
        }

        return resolved;
    }

    public static string GetDatabasePath(string dataDirectory)
    {
        return Path.Combine(dataDirectory, DatabaseFileName);
    }

    // Copies a pre-relocation database (old %LOCALAPPDATA%\SylvaNote layout) into the
    // resolved directory the first time the relocated build runs. Copy, not move, so
    // the original survives as a fallback until the user removes it. Only the desktop
    // client calls this: it runs unsandboxed as the real user, whereas a spawned MCP
    // binary can see a virtualized AppData shadow rather than the real files.
    public static void MigrateLegacyDatabase(string dataDirectory)
    {
        string target = GetDatabasePath(dataDirectory);
        string legacyDatabase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            LegacyDirectoryName,
            DatabaseFileName);

        // An existing target (including a manual copy) is authoritative; migrate only
        // into a location that has no database yet.
        if (!File.Exists(target) && File.Exists(legacyDatabase))
        {
            Directory.CreateDirectory(dataDirectory);

            // -wal/-shm hold pages not yet checkpointed into the main file; copying the
            // main DB alone would drop the most recent edits.
            CopyIfPresent(legacyDatabase, target);
            CopyIfPresent(legacyDatabase + "-wal", target + "-wal");
            CopyIfPresent(legacyDatabase + "-shm", target + "-shm");
        }
    }

    private static void CopyIfPresent(string source, string destination)
    {
        if (File.Exists(source))
        {
            File.Copy(source, destination, false);
        }
    }
}
