using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Lorestead.Core.Entities;
using Lorestead.Core.Sync;

namespace Lorestead.Core.DataAccess
{
    public sealed class TaskRepository
    {
        private readonly ConnectionManager _connectionManager;
        private readonly string _deviceId;
        private readonly int _historyRetention;

        public TaskRepository(ConnectionManager connectionManager, string deviceId, int historyRetention = 50)
        {
            _connectionManager = connectionManager;
            _deviceId = deviceId;
            _historyRetention = historyRetention;
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
            // Normalized here, not per caller, so the payload every writer (dialog,
            // MCP) ships is already clean and peers apply it verbatim.
            task.Labels = TaskLabels.Normalize(task.Labels);

            UpsertWithin(connection, transaction, task);
            ReplaceNoteLinksWithin(connection, transaction, task.Id, task.NoteIds);
            ReplaceLabelsWithin(connection, transaction, task.Id, task.Labels);
            NoteLinkRebuilder.RebuildForTaskWithin(connection, transaction, task.Id, task.Body);

            ChangeLogRepository.AppendAndPruneWithin(connection, transaction, new ChangeLogEntry
            {
                ItemType = ItemTypes.Task,
                ItemId = task.Id,
                Op = ChangeOps.Upsert,
                Payload = PayloadJson.Serialize(task),
                BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, ItemTypes.Task, task.Id),
                DeviceId = _deviceId,
                ChangedAt = now,
            }, _historyRetention);

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
                task.Labels = GetLabels(connection, task.Id);
            }
            return task;
        }

        // Tasks have no trash UI - delete tombstones immediately (data.md tombstone
        // flag; sync propagates it like any other change).
        public void Delete(string id)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            TombstoneWithin(connection, transaction, id, _deviceId, Timestamps.UtcNowIso(), _historyRetention);

            transaction.Commit();
        }

        // One query for the whole kanban view; NoteIds and Labels are left empty -
        // cards don't show links, and labels come from GetLabelsForBoard in one
        // grouped query rather than a read per task. The edit dialog loads the
        // full task.
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
        // One grouped query for the whole board (same shape as the attachment counts) -
        // GetActiveForBoard deliberately leaves NoteIds null, so the cards cannot count
        // links themselves without an N+1. Trashed notes are excluded: the count should
        // match what the task dialog can actually show.
        public Dictionary<string, int> CountNoteLinksForBoard(string boardId)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = @"
                SELECT tn.task_id, COUNT(*)
                FROM task_note tn
                JOIN task t ON t.id = tn.task_id
                JOIN board_column bc ON bc.id = t.column_id
                JOIN note n ON n.id = tn.note_id
                WHERE bc.board_id = @board_id AND n.deleted = 0
                GROUP BY tn.task_id";
            select.Parameters.AddWithValue("@board_id", boardId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                counts[reader.GetString(0)] = reader.GetInt32(1);
            }
            return counts;
        }

        // Labels per active task on a board, in the order they were given. Same
        // one-query shape as the counts above; the board load stitches them onto
        // the summaries.
        public Dictionary<string, List<string>> GetLabelsForBoard(string boardId)
        {
            Dictionary<string, List<string>> labels = new Dictionary<string, List<string>>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = @"
                SELECT tl.task_id, tl.label
                FROM task_label tl
                JOIN task t ON t.id = tl.task_id
                JOIN board_column bc ON bc.id = t.column_id
                WHERE bc.board_id = @board_id AND t.deleted = 0
                ORDER BY tl.task_id, tl.ord";
            select.Parameters.AddWithValue("@board_id", boardId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                string taskId = reader.GetString(0);
                if (!labels.TryGetValue(taskId, out List<string> list))
                {
                    list = new List<string>();
                    labels[taskId] = list;
                }
                list.Add(reader.GetString(1));
            }
            return labels;
        }

        // Every label in use on any live task, most used first, for the dialog's
        // suggestions. Spellings that differ only by case fold together.
        public List<string> GetAllLabels()
        {
            List<string> labels = new List<string>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = @"
                SELECT tl.label, COUNT(*) AS uses
                FROM task_label tl
                JOIN task t ON t.id = tl.task_id
                WHERE t.deleted = 0
                GROUP BY tl.label COLLATE NOCASE
                ORDER BY uses DESC, tl.label COLLATE NOCASE";
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                labels.Add(reader.GetString(0));
            }
            return labels;
        }

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
                task.Labels = GetLabels(connection, task.Id);
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

        // task_label rows are rewritten from the payload's full label list, same as
        // the note links; ord records list order so reads come back as given.
        public static void ReplaceLabelsWithin(SqliteConnection connection, SqliteTransaction transaction, string taskId, List<string> labels)
        {
            using (SqliteCommand delete = connection.CreateCommand())
            {
                delete.CommandText = "DELETE FROM task_label WHERE task_id = @task_id";
                delete.Parameters.AddWithValue("@task_id", taskId);
                delete.ExecuteNonQuery();
            }

            int ord = 0;
            foreach (string label in labels ?? new List<string>())
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText = "INSERT OR IGNORE INTO task_label (task_id, label, ord) VALUES (@task_id, @label, @ord)";
                insert.Parameters.AddWithValue("@task_id", taskId);
                insert.Parameters.AddWithValue("@label", label);
                insert.Parameters.AddWithValue("@ord", ord++);
                insert.ExecuteNonQuery();
            }
        }

        // Shared by the column/board delete cascades so every tombstone gets its own
        // outbox entry with the task's full state (including its note links and
        // labels).
        public static void TombstoneWithin(SqliteConnection connection, SqliteTransaction transaction, string id, string deviceId, string now, int historyRetention)
        {
            TaskItem task = GetWithin(connection, transaction, id);
            if (task != null && !task.Deleted)
            {
                task.Deleted = true;
                task.UpdatedAt = now;
                task.NoteIds = GetNoteIds(connection, id);
                task.Labels = GetLabels(connection, id);
                UpsertWithin(connection, transaction, task);
                ChangeLogRepository.AppendAndPruneWithin(connection, transaction, new ChangeLogEntry
                {
                    ItemType = ItemTypes.Task,
                    ItemId = task.Id,
                    Op = ChangeOps.Upsert,
                    Payload = PayloadJson.Serialize(task),
                    BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, ItemTypes.Task, task.Id),
                    DeviceId = deviceId,
                    ChangedAt = now,
                }, historyRetention);
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

        private static List<string> GetLabels(SqliteConnection connection, string taskId)
        {
            List<string> labels = new List<string>();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT label FROM task_label WHERE task_id = @task_id ORDER BY ord";
            select.Parameters.AddWithValue("@task_id", taskId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                labels.Add(reader.GetString(0));
            }
            return labels;
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
