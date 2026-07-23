using System;
using System.IO;

namespace SylvaNote.Client;

public class Config
{
    public string DataDirectory { get; set; }
    public string DbFilePath { get; set; }
    public string LogFilePath { get; set; }

    public static Config Create(string appName)
    {
        string dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName);
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(Path.Combine(dataDirectory, "logs"));

        return new Config
        {
            DataDirectory = dataDirectory,
            DbFilePath = Path.Combine(dataDirectory, "sylvanote.db"),
            LogFilePath = Path.Combine(dataDirectory, "logs", "sylvanote.log"),
        };
    }
}
