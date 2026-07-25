using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Sync;

namespace SylvaNote.Core.DataAccess
{
    public sealed class BoardRepository
    {
        private readonly ConnectionManager _connectionManager;
        private readonly string _deviceId;
        private readonly int _historyRetention;

        public BoardRepository(ConnectionManager connectionManager, string deviceId, int historyRetention = 50)
        {
            _connectionManager = connectionManager;
            _deviceId = deviceId;
            _historyRetention = historyRetention;
        }

        public void Save(Board board)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            string now = Timestamps.UtcNowIso();
            if (string.IsNullOrEmpty(board.CreatedAt))
            {
                board.CreatedAt = now;
            }
            board.UpdatedAt = now;

            UpsertWithin(connection, transaction, board);

            ChangeLogRepository.AppendAndPruneWithin(connection, transaction, new ChangeLogEntry
            {
                ItemType = ItemTypes.Board,
                ItemId = board.Id,
                Op = ChangeOps.Upsert,
                Payload = PayloadJson.Serialize(board),
                BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, ItemTypes.Board, board.Id),
                DeviceId = _deviceId,
                ChangedAt = now,
            }, _historyRetention);

            transaction.Commit();
        }

        public Board Get(string id)
        {
            Board board = null;
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " WHERE id = @id";
            select.Parameters.AddWithValue("@id", id);
            using SqliteDataReader reader = select.ExecuteReader();
            if (reader.Read())
            {
                board = ReadBoard(reader);
            }
            return board;
        }

        public List<Board> GetAll()
        {
            List<Board> boards = new List<Board>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " ORDER BY position";
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                boards.Add(ReadBoard(reader));
            }
            return boards;
        }

        public List<Board> GetActive()
        {
            List<Board> boards = new List<Board>();
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " WHERE deleted = 0 ORDER BY position";
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                boards.Add(ReadBoard(reader));
            }
            return boards;
        }

        // Boards have no trash - delete tombstones the board and cascades to its
        // columns and their tasks, each with its own outbox entry (sync-safe).
        public void DeleteCascade(string id)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            string now = Timestamps.UtcNowIso();
            Board board = GetWithin(connection, transaction, id);
            if (board != null && !board.Deleted)
            {
                board.Deleted = true;
                board.UpdatedAt = now;
                UpsertWithin(connection, transaction, board);
                ChangeLogRepository.AppendAndPruneWithin(connection, transaction, new ChangeLogEntry
                {
                    ItemType = ItemTypes.Board,
                    ItemId = board.Id,
                    Op = ChangeOps.Upsert,
                    Payload = PayloadJson.Serialize(board),
                    BaseSeq = ChangeLogRepository.MaxSeqForItemWithin(connection, transaction, ItemTypes.Board, board.Id),
                    DeviceId = _deviceId,
                    ChangedAt = now,
                }, _historyRetention);

                foreach (string columnId in BoardColumnRepository.ReadActiveIdsForBoardWithin(connection, transaction, id))
                {
                    BoardColumnRepository.TombstoneCascadeWithin(connection, transaction, columnId, _deviceId, now, _historyRetention);
                }
            }

            transaction.Commit();
        }

        // Board positions share one keyspace across active and tombstoned rows.
        public string GetMaxPosition()
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT MAX(position) FROM board";
            object result = select.ExecuteScalar();
            return result is string value ? value : null;
        }

        public bool PositionExists(string position)
        {
            using SqliteConnection connection = _connectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = "SELECT 1 FROM board WHERE position = @position LIMIT 1";
            select.Parameters.AddWithValue("@position", position);
            return select.ExecuteScalar() != null;
        }

        private static Board GetWithin(SqliteConnection connection, SqliteTransaction transaction, string id)
        {
            Board board = null;
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = SelectSql + " WHERE id = @id";
            select.Parameters.AddWithValue("@id", id);
            using SqliteDataReader reader = select.ExecuteReader();
            if (reader.Read())
            {
                board = ReadBoard(reader);
            }
            return board;
        }

        public static void UpsertWithin(SqliteConnection connection, SqliteTransaction transaction, Board board)
        {
            using SqliteCommand upsert = connection.CreateCommand();
            upsert.CommandText = @"
                INSERT INTO board (id, name, position, deleted, created_at, updated_at)
                VALUES (@id, @name, @position, @deleted, @created_at, @updated_at)
                ON CONFLICT (id) DO UPDATE SET
                    name = excluded.name, position = excluded.position, deleted = excluded.deleted,
                    created_at = excluded.created_at, updated_at = excluded.updated_at";
            upsert.Parameters.AddWithValue("@id", board.Id);
            upsert.Parameters.AddWithValue("@name", board.Name ?? string.Empty);
            upsert.Parameters.AddWithValue("@position", board.Position);
            upsert.Parameters.AddWithValue("@deleted", board.Deleted ? 1 : 0);
            upsert.Parameters.AddWithValue("@created_at", board.CreatedAt);
            upsert.Parameters.AddWithValue("@updated_at", board.UpdatedAt);
            upsert.ExecuteNonQuery();
        }

        public static void DeleteRowWithin(SqliteConnection connection, SqliteTransaction transaction, string id)
        {
            using SqliteCommand delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM board WHERE id = @id";
            delete.Parameters.AddWithValue("@id", id);
            delete.ExecuteNonQuery();
        }

        private const string SelectSql =
            "SELECT id, name, position, deleted, created_at, updated_at FROM board";

        private static Board ReadBoard(SqliteDataReader reader)
        {
            return new Board
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                Position = reader.GetString(2),
                Deleted = reader.GetInt64(3) != 0,
                CreatedAt = reader.GetString(4),
                UpdatedAt = reader.GetString(5),
            };
        }
    }
}
