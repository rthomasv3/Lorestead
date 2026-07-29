using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Lorestead.Core.Entities;
using Lorestead.Core.Search;
using Xunit;

namespace Lorestead.IntegrationTests
{
    public sealed class FtsTests
    {
        [Fact]
        public void InsertedNoteIsSearchable()
        {
            using TestDb db = new TestDb();
            Note note = Items.Note("Quantum notes", "about flux capacitors");
            db.Notes.Save(note);

            List<SearchResult> byTitle = db.Search.SearchNotes("quantum");
            Assert.Single(byTitle);
            Assert.Equal(note.Id, byTitle[0].Id);

            List<SearchResult> byBody = db.Search.SearchNotes("flux");
            Assert.Single(byBody);
            Assert.Contains("[flux]", byBody[0].Snippet);
        }

        [Fact]
        public void UpdateKeepsIndexInStep()
        {
            using TestDb db = new TestDb();
            Note note = Items.Note("First", "alpha content");
            db.Notes.Save(note);

            note.Body = "beta content";
            db.Notes.Save(note);

            Assert.Empty(db.Search.SearchNotes("alpha"));
            Assert.Single(db.Search.SearchNotes("beta"));
        }

        [Fact]
        public void TrashedNotesAreExcludedFromSearch()
        {
            using TestDb db = new TestDb();
            Note note = Items.Note("Secret", "hidden treasure");
            db.Notes.Save(note);
            note.Deleted = true;
            db.Notes.Save(note);

            Assert.Empty(db.Search.SearchNotes("treasure"));
        }

        [Fact]
        public void HardDeleteRemovesFromIndex()
        {
            using TestDb db = new TestDb();
            Note note = Items.Note("Gone", "vanishing act");
            db.Notes.Save(note);

            using (SqliteConnection connection = db.ConnectionManager.CreateConnection())
            {
                using SqliteCommand delete = connection.CreateCommand();
                delete.CommandText = "DELETE FROM note WHERE id = @id";
                delete.Parameters.AddWithValue("@id", note.Id);
                delete.ExecuteNonQuery();
            }

            Assert.Empty(db.Search.SearchNotes("vanishing"));
            Assert.Equal(0L, CountFtsRows(db, "note_fts", "vanishing"));
        }

        [Fact]
        public void TasksHaveTheirOwnIndex()
        {
            using TestDb db = new TestDb();
            Board board = Items.Board();
            db.Boards.Save(board);
            BoardColumn column = Items.Column(board.Id);
            db.Columns.Save(column);
            TaskItem task = Items.Task(column.Id, "Water plants", "the succulents too");
            db.Tasks.Save(task);

            List<SearchResult> results = db.Search.SearchTasks("succulents");
            Assert.Single(results);
            Assert.Equal(task.Id, results[0].Id);
            Assert.Empty(db.Search.SearchNotes("succulents"));
        }

        private static long CountFtsRows(TestDb db, string ftsTable, string term)
        {
            using SqliteConnection connection = db.ConnectionManager.CreateConnection();
            using SqliteCommand select = connection.CreateCommand();
            select.CommandText = $"SELECT COUNT(*) FROM {ftsTable} WHERE {ftsTable} MATCH @term";
            select.Parameters.AddWithValue("@term", term);
            return (long)select.ExecuteScalar();
        }
    }
}
