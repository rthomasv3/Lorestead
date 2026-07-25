using System.IO;
using SylvaNote.Core.DataAccess;

namespace SylvaNote.Client;

public class Config
{
    public string DataDirectory { get; set; }
    public string DbFilePath { get; set; }
    public string LogFilePath { get; set; }

    public static Config Create()
    {
        string dataDirectory = LocalDataPaths.ResolveDataDirectory();
        LocalDataPaths.MigrateLegacyDatabase(dataDirectory);
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(Path.Combine(dataDirectory, "logs"));

        return new Config
        {
            DataDirectory = dataDirectory,
            DbFilePath = LocalDataPaths.GetDatabasePath(dataDirectory),
            LogFilePath = Path.Combine(dataDirectory, "logs", "sylvanote.log"),
        };
    }
}
