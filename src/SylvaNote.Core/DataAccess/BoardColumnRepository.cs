using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Sync;

namespace SylvaNote.Core.DataAccess
{
    public sealed class BoardColumnRepository
    {
        private readonly ConnectionManager _connectionManager;
        private readonly string _deviceId;

        public BoardColumnRepository(ConnectionManager connectionManager, string deviceId)
        {
            _connectionManager = connectionManager;
            _deviceId = deviceId;
        }

        public void Save(BoardColumn column)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            string now = Timestamps.UtcNowIso();
            if (string.IsNullOrEmpty(column.CreatedAt))
            {
                column.CreatedAt = now;
            }
            column.UpdatedAt = now;

            UpsertWithin(connection, transaction, column);

            ChangeLogRepository.AppendWithin(connection, transaction, new ChangeLogEntry
            {
                ItemType = ItemTypes.Column,
                ItemId = column.Id,
                Op = ChangeOps.Upsert,
                Payload = PayloadJson.Serialize(column),
                BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, ItemTypes.Column, column.Id),
                DeviceId = _deviceId,
                ChangedAt = now,
            });

            transaction.Commit();
        }

        public BoardColumn Get(string id)
        {
            BoardColumn column = null;
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " WHERE id = @id";
            select.Parameters.AddWithValue("@id", id);
            using SqliteDataReader reader = select.ExecuteReader();
            if (reader.Read())
            {
                column = ReadColumn(reader);
            }
            return column;
        }

        // Deleting a list tombstones its tasks with it - same subtree-cascade shape
        // as note trash, each row its own outbox entry.
        public void DeleteCascade(string id)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            TombstoneCascadeWithin(connection, transaction, id, _deviceId, Timestamps.UtcNowIso());

            transaction.Commit();
        }

        public List<BoardColumn> GetActiveForBoard(string boardId)
        {
            List<BoardColumn> columns = new List<BoardColumn>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " WHERE board_id = @board_id AND deleted = 0 ORDER BY position";
            select.Parameters.AddWithValue("@board_id", boardId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(ReadColumn(reader));
            }
            return columns;
        }

        public string GetMaxPosition(string boardId)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT MAX(position) FROM board_column WHERE board_id = @board_id";
            select.Parameters.AddWithValue("@board_id", boardId);
            object result = select.ExecuteScalar();
            return result is string value ? value : null;
        }

        public bool PositionExists(string boardId, string position)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT 1 FROM board_column WHERE board_id = @board_id AND position = @position LIMIT 1";
            select.Parameters.AddWithValue("@board_id", boardId);
            select.Parameters.AddWithValue("@position", position);
            return select.ExecuteScalar() != null;
        }

        public List<BoardColumn> GetForBoard(string boardId)
        {
            List<BoardColumn> columns = new List<BoardColumn>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " WHERE board_id = @board_id ORDER BY position";
            select.Parameters.AddWithValue("@board_id", boardId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(ReadColumn(reader));
            }
            return columns;
        }

        public static void UpsertWithin(SqliteConnection connection, SqliteTransaction transaction, BoardColumn column)
        {
            using SqliteCommand upsert = connection.CreateCommand();
            upsert.CommandText = @"
                INSERT INTO board_column (id, board_id, name, position, deleted, created_at, updated_at)
                VALUES (@id, @board_id, @name, @position, @deleted, @created_at, @updated_at)
                ON CONFLICT (id) DO UPDATE SET
                    board_id = excluded.board_id, name = excluded.name, position = excluded.position,
                    deleted = excluded.deleted, created_at = excluded.created_at, updated_at = excluded.updated_at";
            upsert.Parameters.AddWithValue("@id", column.Id);
            upsert.Parameters.AddWithValue("@board_id", column.BoardId);
            upsert.Parameters.AddWithValue("@name", column.Name ?? string.Empty);
            upsert.Parameters.AddWithValue("@position", column.Position);
            upsert.Parameters.AddWithValue("@deleted", column.Deleted ? 1 : 0);
            upsert.Parameters.AddWithValue("@created_at", column.CreatedAt);
            upsert.Parameters.AddWithValue("@updated_at", column.UpdatedAt);
            upsert.ExecuteNonQuery();
        }

        public static void TombstoneCascadeWithin(SqliteConnection connection, SqliteTransaction transaction, string id, string deviceId, string now)
        {
            BoardColumn column = GetWithin(connection, transaction, id);
            if (column != null && !column.Deleted)
            {
                column.Deleted = true;
                column.UpdatedAt = now;
                UpsertWithin(connection, transaction, column);
                ChangeLogRepository.AppendWithin(connection, transaction, new ChangeLogEntry
                {
                    ItemType = ItemTypes.Column,
                    ItemId = column.Id,
                    Op = ChangeOps.Upsert,
                    Payload = PayloadJson.Serialize(column),
                    BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, ItemTypes.Column, column.Id),
                    DeviceId = deviceId,
                    ChangedAt = now,
                });

                foreach (string taskId in TaskRepository.ReadActiveIdsForColumnWithin(connection, transaction, id))
                {
                    TaskRepository.TombstoneWithin(connection, transaction, taskId, deviceId, now);
                }
            }
        }

        public static List<string> ReadActiveIdsForBoardWithin(SqliteConnection connection, SqliteTransaction transaction, string boardId)
        {
            List<string> ids = new List<string>();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT id FROM board_column WHERE board_id = @board_id AND deleted = 0";
            select.Parameters.AddWithValue("@board_id", boardId);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                ids.Add(reader.GetString(0));
            }
            return ids;
        }

        private static BoardColumn GetWithin(SqliteConnection connection, SqliteTransaction transaction, string id)
        {
            BoardColumn column = null;
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " WHERE id = @id";
            select.Parameters.AddWithValue("@id", id);
            using SqliteDataReader reader = select.ExecuteReader();
            if (reader.Read())
            {
                column = ReadColumn(reader);
            }
            return column;
        }

        public static void DeleteRowWithin(SqliteConnection connection, SqliteTransaction transaction, string id)
        {
            using SqliteCommand delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM board_column WHERE id = @id";
            delete.Parameters.AddWithValue("@id", id);
            delete.ExecuteNonQuery();
        }

        private const string SelectSql =
            "SELECT id, board_id, name, position, deleted, created_at, updated_at FROM board_column";

        private static BoardColumn ReadColumn(SqliteDataReader reader)
        {
            return new BoardColumn
            {
                Id = reader.GetString(0),
                BoardId = reader.GetString(1),
                Name = reader.GetString(2),
                Position = reader.GetString(3),
                Deleted = reader.GetInt64(4) != 0,
                CreatedAt = reader.GetString(5),
                UpdatedAt = reader.GetString(6),
            };
        }
    }
}
