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
}
