using Microsoft.Data.Sqlite;

namespace Lorestead.Core.DataAccess.Migrations
{
    // Labels are plain strings on a task, no label entity: they ride the task
    // payload like note links and are rewritten from it on every apply, so the
    // table is derived state on both client and server. ord keeps the order the
    // user typed them in; the primary key makes a label unique per task.
    public sealed class Db009_TaskLabels : IMigration
    {
        public int Version => 9;

        public void Up(SqliteConnection connection)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE task_label (
                    task_id TEXT    NOT NULL REFERENCES task (id) ON DELETE CASCADE,
                    label   TEXT    NOT NULL,
                    ord     INTEGER NOT NULL,
                    PRIMARY KEY (task_id, label)
                ) WITHOUT ROWID;
                CREATE INDEX idx_task_label_label ON task_label (label);";
            command.ExecuteNonQuery();
        }
    }
}
