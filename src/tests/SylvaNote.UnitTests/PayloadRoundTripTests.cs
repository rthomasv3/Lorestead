using System.Collections.Generic;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Sync;
using Xunit;

namespace SylvaNote.UnitTests
{
    public sealed class PayloadRoundTripTests
    {
        [Fact]
        public void NoteRoundTripsWithCamelCaseNames()
        {
            Note note = new Note
            {
                Id = "0198c0de-0000-7000-8000-000000000001",
                ParentId = "0198c0de-0000-7000-8000-000000000002",
                Title = "Title",
                Body = "Body with [link](note://0198c0de-0000-7000-8000-000000000003)",
                Position = "V",
                Type = NoteType.Template,
                Deleted = true,
                CreatedAt = "2026-07-23T09:00:00.0000000Z",
                UpdatedAt = "2026-07-23T09:01:00.0000000Z",
            };

            string json = PayloadJson.Serialize(note);
            Assert.Contains("\"parentId\"", json);
            Assert.Contains("\"createdAt\"", json);

            Note back = PayloadJson.Deserialize<Note>(json);
            Assert.Equal(note.Id, back.Id);
            Assert.Equal(note.ParentId, back.ParentId);
            Assert.Equal(note.Title, back.Title);
            Assert.Equal(note.Body, back.Body);
            Assert.Equal(note.Position, back.Position);
            Assert.Equal(note.Type, back.Type);
            Assert.Equal(note.Deleted, back.Deleted);
            Assert.Equal(note.CreatedAt, back.CreatedAt);
            Assert.Equal(note.UpdatedAt, back.UpdatedAt);
        }

        [Fact]
        public void NullParentRoundTrips()
        {
            Note note = new Note
            {
                Id = "0198c0de-0000-7000-8000-000000000001",
                Title = "Root",
                Body = "",
                Position = "V",
                CreatedAt = "2026-07-23T09:00:00.0000000Z",
                UpdatedAt = "2026-07-23T09:00:00.0000000Z",
            };
            Note back = PayloadJson.Deserialize<Note>(PayloadJson.Serialize(note));
            Assert.Null(back.ParentId);
        }

        [Fact]
        public void TaskRoundTripsNoteIds()
        {
            TaskItem task = new TaskItem
            {
                Id = "0198c0de-0000-7000-8000-000000000010",
                ColumnId = "0198c0de-0000-7000-8000-000000000011",
                Title = "Do it",
                Body = "Steps",
                Position = "V",
                CreatedAt = "2026-07-23T09:00:00.0000000Z",
                UpdatedAt = "2026-07-23T09:00:00.0000000Z",
                NoteIds = new List<string> { "0198c0de-0000-7000-8000-000000000001", "0198c0de-0000-7000-8000-000000000002" },
            };

            string json = PayloadJson.Serialize(task);
            Assert.Contains("\"noteIds\"", json);

            TaskItem back = PayloadJson.Deserialize<TaskItem>(json);
            Assert.Equal(task.NoteIds, back.NoteIds);
            Assert.Equal(task.ColumnId, back.ColumnId);
        }

        [Fact]
        public void BoardAndColumnRoundTrip()
        {
            Board board = new Board
            {
                Id = "0198c0de-0000-7000-8000-000000000020",
                Name = "Work",
                Position = "V",
                CreatedAt = "2026-07-23T09:00:00.0000000Z",
                UpdatedAt = "2026-07-23T09:00:00.0000000Z",
            };
            Board boardBack = PayloadJson.Deserialize<Board>(PayloadJson.Serialize(board));
            Assert.Equal(board.Name, boardBack.Name);

            BoardColumn column = new BoardColumn
            {
                Id = "0198c0de-0000-7000-8000-000000000021",
                BoardId = board.Id,
                Name = "To Do",
                Position = "V",
                CreatedAt = "2026-07-23T09:00:00.0000000Z",
                UpdatedAt = "2026-07-23T09:00:00.0000000Z",
            };
            BoardColumn columnBack = PayloadJson.Deserialize<BoardColumn>(PayloadJson.Serialize(column));
            Assert.Equal(column.BoardId, columnBack.BoardId);
            Assert.Equal(column.Name, columnBack.Name);
        }

        [Fact]
        public void AttachmentRoundTrips()
        {
            Attachment attachment = new Attachment
            {
                Id = "0198c0de-0000-7000-8000-000000000030",
                NoteId = "0198c0de-0000-7000-8000-000000000001",
                Filename = "shot.png",
                MimeType = "image/png",
                SizeBytes = 12345,
                CreatedAt = "2026-07-23T09:00:00.0000000Z",
                UpdatedAt = "2026-07-23T09:00:00.0000000Z",
            };
            Attachment back = PayloadJson.Deserialize<Attachment>(PayloadJson.Serialize(attachment));
            Assert.Equal(attachment.NoteId, back.NoteId);
            Assert.Null(back.TaskId);
            Assert.Equal(attachment.SizeBytes, back.SizeBytes);
        }

        [Fact]
        public void ChangeLogEntryRoundTrips()
        {
            ChangeLogEntry entry = new ChangeLogEntry
            {
                Seq = 42,
                ItemType = ItemTypes.Note,
                ItemId = "0198c0de-0000-7000-8000-000000000001",
                Op = ChangeOps.Upsert,
                Payload = "{\"id\":\"x\"}",
                BaseSeq = null,
                SupersededConcurrent = true,
                DeviceId = "0198c0de-0000-7000-8000-0000000000aa",
                ChangedAt = "2026-07-23T09:00:00.0000000Z",
            };
            ChangeLogEntry back = PayloadJson.Deserialize<ChangeLogEntry>(PayloadJson.Serialize(entry));
            Assert.Equal(entry.Seq, back.Seq);
            Assert.Null(back.BaseSeq);
            Assert.Equal(entry.Payload, back.Payload);
            Assert.True(back.SupersededConcurrent);
        }
    }
}
