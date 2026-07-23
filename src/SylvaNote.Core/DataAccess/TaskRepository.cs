using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Sync;

namespace SylvaNote.Core.DataAccess
{
    public sealed class TaskRepository
    {
        private readonly ConnectionManager _connectionManager;
        private readonly string _deviceId;

        public TaskRepository(ConnectionManager connectionManager, string deviceId)
        {
            _connectionManager = connectionManager;
            _deviceId = deviceId;
        }

        public void Save(TaskItem task)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            string now = Timestamps.UtcNowIso();
            if (string.IsNullOrEmpty(task.CreatedAt))
            {
                task.CreatedAt = now;
            }
            task.UpdatedAt = now;

            UpsertWithin(connection, transaction, task);
            ReplaceNoteLinksWithin(connection, transaction, task.Id, task.NoteIds);
            NoteLinkRebuilder.RebuildForTaskWithin(connection, transaction, task.Id, task.Body);

            ChangeLogRepository.AppendWithin(connection, transaction, new ChangeLogEntry
            {
                ItemType = ItemTypes.Task,
                ItemId = task.Id,
                Op = ChangeOps.Upsert,
                Payload = PayloadJson.Serialize(task),
                BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, ItemTypes.Task, task.Id),
                DeviceId = _deviceId,
                ChangedAt = now,
            });

            transaction.Commit();
        }

        public TaskItem Get(string id)
        {
            TaskItem task = null;
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using (SqliteCommand select = connection.CreateCommand())
            {
                select.CommandText = SelectSql + " WHERE id = @id";
                select.Parameters.AddWithValue("@id", id);
                using SqliteDataReader reader = select.ExecuteReader();
                if (reader.Read())
                {
                    task = ReadTask(reader);
                }
            }
            if (task != null)
            {
                task.NoteIds = GetNoteIds(connection, task.Id);
            }
            return task;
        }

        // Tasks have no trash UI - delete tombstones immediately (data.md tombstone
        // flag; sync propagates it like any other change).
        public void Delete(string id)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            TombstoneWithin(connection, transaction, id, _deviceId, Timestamps.UtcNowIso());

            transaction.Commit();
        }

        // One query for the whole kanban view; NoteIds are left null - cards don't
        // show links, the edit dialog loads the full task.
        public List<TaskItem> GetActiveForBoard(string boardId)
        {
            List<TaskItem> tasks = new List<TaskItem>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = @"
                SELECT task.id, task.column_id, task.title, task.body, task.position, task.deleted, task.created_at, task.updated_at
                FROM task
                JOIN board_column bc ON bc.id = task.column_id
                WHERE bc.board_id = @board_id AND task.deleted = 0 AND bc.deleted = 0
                ORDER BY task.position";
            select.Parameters.AddWithValue("@board_id", boardId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                tasks.Add(ReadTask(reader));
            }
            return tasks;
        }

        // Position uniqueness spans ALL tasks in the column - tombstoned rows share
        // the fractional keyspace even though they never render.
        public string GetMaxPosition(string columnId)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT MAX(position) FROM task WHERE column_id = @column_id";
            select.Parameters.AddWithValue("@column_id", columnId);
            object result = select.ExecuteScalar();
            return result is string value ? value : null;
        }

        public bool PositionExists(string columnId, string position)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT 1 FROM task WHERE column_id = @column_id AND position = @position LIMIT 1";
            select.Parameters.AddWithValue("@column_id", columnId);
            select.Parameters.AddWithValue("@position", position);
            return select.ExecuteScalar() != null;
        }

        public List<TaskItem> GetForColumn(string columnId)
        {
            List<TaskItem> tasks = new List<TaskItem>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using (SqliteCommand select = connection.CreateCommand())
            {
                select.CommandText = SelectSql + " WHERE column_id = @column_id ORDER BY position";
                select.Parameters.AddWithValue("@column_id", columnId);
                using SqliteDataReader reader = select.ExecuteReader();
                while (reader.Read())
                {
                    tasks.Add(ReadTask(reader));
                }
            }
            foreach (TaskItem task in tasks)
            {
                task.NoteIds = GetNoteIds(connection, task.Id);
            }
            return tasks;
        }

        public static void UpsertWithin(SqliteConnection connection, SqliteTransaction transaction, TaskItem task)
        {
            using SqliteCommand upsert = connection.CreateCommand();
            upsert.CommandText = @"
                INSERT INTO task (id, column_id, title, body, position, deleted, created_at, updated_at)
                VALUES (@id, @column_id, @title, @body, @position, @deleted, @created_at, @updated_at)
                ON CONFLICT (id) DO UPDATE SET
                    column_id = excluded.column_id, title = excluded.title, body = excluded.body,
                    position = excluded.position, deleted = excluded.deleted,
                    created_at = excluded.created_at, updated_at = excluded.updated_at";
            upsert.Parameters.AddWithValue("@id", task.Id);
            upsert.Parameters.AddWithValue("@column_id", task.ColumnId);
            upsert.Parameters.AddWithValue("@title", task.Title ?? string.Empty);
            upsert.Parameters.AddWithValue("@body", task.Body ?? string.Empty);
            upsert.Parameters.AddWithValue("@position", task.Position);
            upsert.Parameters.AddWithValue("@deleted", task.Deleted ? 1 : 0);
            upsert.Parameters.AddWithValue("@created_at", task.CreatedAt);
            upsert.Parameters.AddWithValue("@updated_at", task.UpdatedAt);
            upsert.ExecuteNonQuery();
        }

        // task_note rows are rewritten from the payload's full link list (LWW full item
        // state); targets missing locally are skipped, same policy as note_link.
        public static void ReplaceNoteLinksWithin(SqliteConnection connection, SqliteTransaction transaction, string taskId, List<string> noteIds)
        {
            using (SqliteCommand delete = connection.CreateCommand())
            {
                delete.CommandText = "DELETE FROM task_note WHERE task_id = @task_id";
                delete.Parameters.AddWithValue("@task_id", taskId);
                delete.ExecuteNonQuery();
            }

            foreach (string noteId in noteIds ?? new List<string>())
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText = @"
                    INSERT OR IGNORE INTO task_note (task_id, note_id)
                    SELECT @task_id, @note_id WHERE EXISTS (SELECT 1 FROM note WHERE id = @note_id)";
                insert.Parameters.AddWithValue("@task_id", taskId);
                insert.Parameters.AddWithValue("@note_id", noteId);
                insert.ExecuteNonQuery();
            }
        }

        // Shared by the column/board delete cascades so every tombstone gets its own
        // outbox entry with the task's full state (including its note links).
        public static void TombstoneWithin(SqliteConnection connection, SqliteTransaction transaction, string id, string deviceId, string now)
        {
            TaskItem task = GetWithin(connection, transaction, id);
            if (task != null && !task.Deleted)
            {
                task.Deleted = true;
                task.UpdatedAt = now;
                task.NoteIds = GetNoteIds(connection, id);
                UpsertWithin(connection, transaction, task);
                ChangeLogRepository.AppendWithin(connection, transaction, new ChangeLogEntry
                {
                    ItemType = ItemTypes.Task,
                    ItemId = task.Id,
                    Op = ChangeOps.Upsert,
                    Payload = PayloadJson.Serialize(task),
                    BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, ItemTypes.Task, task.Id),
                    DeviceId = deviceId,
                    ChangedAt = now,
                });
            }
        }

        public static List<string> ReadActiveIdsForColumnWithin(SqliteConnection connection, SqliteTransaction transaction, string columnId)
        {
            List<string> ids = new List<string>();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT id FROM task WHERE column_id = @column_id AND deleted = 0";
            select.Parameters.AddWithValue("@column_id", columnId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }
            return ids;
        }

        private static TaskItem GetWithin(SqliteConnection connection, SqliteTransaction transaction, string id)
        {
            TaskItem task = null;
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " WHERE id = @id";
            select.Parameters.AddWithValue("@id", id);
            using SqliteDataReader reader = select.ExecuteReader();
            if (reader.Read())
            {
                task = ReadTask(reader);
            }
            return task;
        }

        public static void DeleteRowWithin(SqliteConnection connection, SqliteTransaction transaction, string id)
        {
            using SqliteCommand delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM task WHERE id = @id";
            delete.Parameters.AddWithValue("@id", id);
            delete.ExecuteNonQuery();
        }

        private static List<string> GetNoteIds(SqliteConnection connection, string taskId)
        {
            List<string> noteIds = new List<string>();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT note_id FROM task_note WHERE task_id = @task_id ORDER BY note_id";
            select.Parameters.AddWithValue("@task_id", taskId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                noteIds.Add(reader.GetString(0));
            }
            return noteIds;
        }

        private const string SelectSql =
            "SELECT id, column_id, title, body, position, deleted, created_at, updated_at FROM task";

        private static TaskItem ReadTask(SqliteDataReader reader)
        {
            return new TaskItem
            {
                Id = reader.GetString(0),
                ColumnId = reader.GetString(1),
                Title = reader.GetString(2),
                Body = reader.GetString(3),
                Position = reader.GetString(4),
                Deleted = reader.GetInt64(5) != 0,
                CreatedAt = reader.GetString(6),
                UpdatedAt = reader.GetString(7),
            };
        }
    }
}
