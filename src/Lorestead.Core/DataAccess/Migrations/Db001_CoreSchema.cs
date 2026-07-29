using Microsoft.Data.Sqlite;

namespace Lorestead.Core.DataAccess.Migrations
{
    // Shared schema for both the client DB and the server DB (data.md). The kanban column
    // table is board_column, not the spec's `column` - COLUMN is an SQLite keyword and would
    // force quoting in every statement; change_log.item_type keeps the wire value "column".
    // Tree/board FKs are DEFERRABLE so multi-row ops (subtree purge, batch apply) commit
    // without ordering gymnastics.
    public sealed class Db001_CoreSchema : IMigration
    {
        public int Version => 1;

        public void Up(SqliteConnection connection)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE note (
                    id         TEXT    NOT NULL PRIMARY KEY,
                    parent_id  TEXT    REFERENCES note (id) DEFERRABLE INITIALLY DEFERRED,
                    title      TEXT    NOT NULL DEFAULT '',
                    body       TEXT    NOT NULL DEFAULT '',
                    position   TEXT    NOT NULL,
                    type       INTEGER NOT NULL DEFAULT 0,
                    deleted    INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT    NOT NULL,
                    updated_at TEXT    NOT NULL
                );
                CREATE INDEX idx_note_parent ON note (parent_id);

                CREATE TABLE board (
                    id         TEXT    NOT NULL PRIMARY KEY,
                    name       TEXT    NOT NULL DEFAULT '',
                    position   TEXT    NOT NULL,
                    deleted    INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT    NOT NULL,
                    updated_at TEXT    NOT NULL
                );

                CREATE TABLE board_column (
                    id         TEXT    NOT NULL PRIMARY KEY,
                    board_id   TEXT    NOT NULL REFERENCES board (id) DEFERRABLE INITIALLY DEFERRED,
                    name       TEXT    NOT NULL DEFAULT '',
                    position   TEXT    NOT NULL,
                    deleted    INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT    NOT NULL,
                    updated_at TEXT    NOT NULL
                );
                CREATE INDEX idx_board_column_board ON board_column (board_id);

                CREATE TABLE task (
                    id         TEXT    NOT NULL PRIMARY KEY,
                    column_id  TEXT    NOT NULL REFERENCES board_column (id) DEFERRABLE INITIALLY DEFERRED,
                    title      TEXT    NOT NULL DEFAULT '',
                    body       TEXT    NOT NULL DEFAULT '',
                    position   TEXT    NOT NULL,
                    deleted    INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT    NOT NULL,
                    updated_at TEXT    NOT NULL
                );
                CREATE INDEX idx_task_column ON task (column_id);

                CREATE TABLE task_note (
                    task_id TEXT NOT NULL REFERENCES task (id) ON DELETE CASCADE,
                    note_id TEXT NOT NULL REFERENCES note (id) ON DELETE CASCADE,
                    PRIMARY KEY (task_id, note_id)
                ) WITHOUT ROWID;
                CREATE INDEX idx_task_note_note ON task_note (note_id);

                CREATE TABLE attachment (
                    id         TEXT    NOT NULL PRIMARY KEY,
                    note_id    TEXT    REFERENCES note (id) ON DELETE CASCADE,
                    task_id    TEXT    REFERENCES task (id) ON DELETE CASCADE,
                    filename   TEXT    NOT NULL DEFAULT '',
                    mime_type  TEXT    NOT NULL DEFAULT '',
                    size_bytes INTEGER NOT NULL DEFAULT 0,
                    deleted    INTEGER NOT NULL DEFAULT 0,
                    created_at TEXT    NOT NULL,
                    updated_at TEXT    NOT NULL,
                    CHECK ((note_id IS NULL) + (task_id IS NULL) = 1)
                );
                CREATE INDEX idx_attachment_note ON attachment (note_id);
                CREATE INDEX idx_attachment_task ON attachment (task_id);

                CREATE TABLE attachment_blob (
                    attachment_id TEXT NOT NULL PRIMARY KEY REFERENCES attachment (id) ON DELETE CASCADE,
                    data          BLOB NOT NULL
                );

                CREATE TABLE change_log (
                    id                    INTEGER PRIMARY KEY AUTOINCREMENT,
                    seq                   INTEGER UNIQUE,
                    item_type             TEXT    NOT NULL,
                    item_id               TEXT    NOT NULL,
                    op                    TEXT    NOT NULL,
                    payload               TEXT    NOT NULL DEFAULT '',
                    base_seq              INTEGER,
                    superseded_concurrent INTEGER NOT NULL DEFAULT 0,
                    device_id             TEXT    NOT NULL,
                    changed_at            TEXT    NOT NULL
                );
                CREATE INDEX idx_change_log_item ON change_log (item_type, item_id);

                CREATE TABLE note_link (
                    from_note_id TEXT REFERENCES note (id) ON DELETE CASCADE,
                    from_task_id TEXT REFERENCES task (id) ON DELETE CASCADE,
                    to_note_id   TEXT NOT NULL REFERENCES note (id) ON DELETE CASCADE,
                    CHECK ((from_note_id IS NULL) + (from_task_id IS NULL) = 1)
                );
                CREATE INDEX idx_note_link_to ON note_link (to_note_id);
                CREATE INDEX idx_note_link_from_note ON note_link (from_note_id);
                CREATE INDEX idx_note_link_from_task ON note_link (from_task_id);

                CREATE VIRTUAL TABLE note_fts USING fts5(title, body, content='note', content_rowid='rowid');
                CREATE TRIGGER note_fts_insert AFTER INSERT ON note BEGIN
                    INSERT INTO note_fts (rowid, title, body) VALUES (new.rowid, new.title, new.body);
                END;
                CREATE TRIGGER note_fts_delete AFTER DELETE ON note BEGIN
                    INSERT INTO note_fts (note_fts, rowid, title, body) VALUES ('delete', old.rowid, old.title, old.body);
                END;
                CREATE TRIGGER note_fts_update AFTER UPDATE ON note BEGIN
                    INSERT INTO note_fts (note_fts, rowid, title, body) VALUES ('delete', old.rowid, old.title, old.body);
                    INSERT INTO note_fts (rowid, title, body) VALUES (new.rowid, new.title, new.body);
                END;

                CREATE VIRTUAL TABLE task_fts USING fts5(title, body, content='task', content_rowid='rowid');
                CREATE TRIGGER task_fts_insert AFTER INSERT ON task BEGIN
                    INSERT INTO task_fts (rowid, title, body) VALUES (new.rowid, new.title, new.body);
                END;
                CREATE TRIGGER task_fts_delete AFTER DELETE ON task BEGIN
                    INSERT INTO task_fts (task_fts, rowid, title, body) VALUES ('delete', old.rowid, old.title, old.body);
                END;
                CREATE TRIGGER task_fts_update AFTER UPDATE ON task BEGIN
                    INSERT INTO task_fts (task_fts, rowid, title, body) VALUES ('delete', old.rowid, old.title, old.body);
                    INSERT INTO task_fts (rowid, title, body) VALUES (new.rowid, new.title, new.body);
                END;
            ";
            command.ExecuteNonQuery();
        }
    }
}
