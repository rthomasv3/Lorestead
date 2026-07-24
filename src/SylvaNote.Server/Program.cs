using SylvaNote.Core.DataAccess;
using SylvaNote.Core.DataAccess.Migrations;
using SylvaNote.Core.Sync;
using SylvaNote.Server.Services;

namespace SylvaNote.Server;

internal class Program
{
    static void Main(string[] args)
    {
        // Explicit provider init: the reflection-based auto-init is not AOT-reliable,
        // and this host's provider is SQLCipher (bundle_e_sqlcipher).
        SQLitePCL.Batteries_V2.Init();

        ServerConfig config = ServerConfig.FromEnvironment();

        // Unlike the client (which must boot into Settings on a broken DB), the
        // server fails fast - a crash loop in docker is the visible error surface.
        ConnectionManager connectionManager = new ConnectionManager();
        connectionManager.Open(config.DbFilePath, MigrationSets.Server(), config.DbKey);

        // Startup is the pruning cadence: purge entries are tiny rows, and container
        // restarts (updates, reboots) come far more often than the retention horizon.
        new ChangeLogPruner(connectionManager).PruneExpiredPurgeEntries(config.PurgeRetentionDays);

        ServerApp.Create(config, connectionManager, args).Run();
    }
}
