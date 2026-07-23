using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace SpikeDataVersion
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            int exitCode;

            if (args.Length == 2 && args[0] == "--child")
            {
                exitCode = RunChild(args[1]);
            }
            else
            {
                exitCode = RunParent();
            }

            return exitCode;
        }

        private static int RunParent()
        {
            int exitCode = 1;
            string dbPath = Path.Combine(Path.GetTempPath(), $"spike-dataversion-{Guid.NewGuid():N}.db");

            try
            {
                using (SqliteConnection connection = OpenConnection(dbPath))
                {
                    Execute(connection, "PRAGMA journal_mode=WAL");
                    Execute(connection, "CREATE TABLE item (id TEXT PRIMARY KEY, body TEXT NOT NULL)");

                    long baseline = ReadDataVersion(connection);

                    Execute(connection, "INSERT INTO item (id, body) VALUES ('parent-write', 'from parent')");
                    long afterOwnWrite = ReadDataVersion(connection);
                    if (afterOwnWrite != baseline)
                    {
                        throw new InvalidOperationException(
                            $"data_version ticked on this connection's own write ({baseline} -> {afterOwnWrite}); expected unchanged.");
                    }

                    RunChildProcess(dbPath);

                    long observed = afterOwnWrite;
                    for (int i = 0; i < 150 && observed == afterOwnWrite; i++)
                    {
                        Thread.Sleep(100);
                        observed = ReadDataVersion(connection);
                    }

                    if (observed == afterOwnWrite)
                    {
                        throw new InvalidOperationException(
                            "data_version did not change within 15s after another process committed to the same WAL DB.");
                    }

                    long rowCount = (long)ExecuteScalar(connection, "SELECT COUNT(*) FROM item");
                    if (rowCount != 2)
                    {
                        throw new InvalidOperationException($"Expected 2 rows after child write, found {rowCount}.");
                    }

                    Console.WriteLine($"PASS: data_version ticked ({afterOwnWrite} -> {observed}) on a cross-process write and stayed flat on own writes.");
                    exitCode = 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: {ex}");
            }
            finally
            {
                TryDelete(dbPath);
                TryDelete(dbPath + "-wal");
                TryDelete(dbPath + "-shm");
            }

            return exitCode;
        }

        private static int RunChild(string dbPath)
        {
            int exitCode = 0;

            try
            {
                using (SqliteConnection connection = OpenConnection(dbPath))
                {
                    Execute(connection, "INSERT INTO item (id, body) VALUES ('child-write', 'from child')");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CHILD FAIL: {ex}");
                exitCode = 1;
            }

            return exitCode;
        }

        private static void RunChildProcess(string dbPath)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("--child");
            startInfo.ArgumentList.Add(dbPath);

            using (Process child = Process.Start(startInfo))
            {
                if (!child.WaitForExit(30000))
                {
                    child.Kill();
                    throw new InvalidOperationException("Child writer process timed out.");
                }

                if (child.ExitCode != 0)
                {
                    throw new InvalidOperationException($"Child writer process exited with {child.ExitCode}.");
                }
            }
        }

        private static SqliteConnection OpenConnection(string dbPath)
        {
            SqliteConnection connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            connection.Open();
            Execute(connection, "PRAGMA busy_timeout=5000");
            return connection;
        }

        private static void Execute(SqliteConnection connection, string sql)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        private static object ExecuteScalar(SqliteConnection connection, string sql)
        {
            object result;
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = sql;
                result = command.ExecuteScalar();
            }
            return result;
        }

        private static long ReadDataVersion(SqliteConnection connection)
        {
            return (long)ExecuteScalar(connection, "PRAGMA data_version");
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort temp cleanup; a straggling handle on Windows is not a spike failure.
            }
        }
    }
}
