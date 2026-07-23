using System;
using System.Collections.Generic;
using SylvaNote.Core.Entities;

namespace SylvaNote.IntegrationTests
{
    public static class Items
    {
        public static string NewId()
        {
            return Guid.CreateVersion7().ToString();
        }

        public static Note Note(string title = "Note", string body = "", string parentId = null)
        {
            return new Note
            {
                Id = NewId(),
                ParentId = parentId,
                Title = title,
                Body = body,
                Position = "V",
            };
        }

        public static Board Board(string name = "Board")
        {
            return new Board
            {
                Id = NewId(),
                Name = name,
                Position = "V",
            };
        }

        public static BoardColumn Column(string boardId, string name = "Column")
        {
            return new BoardColumn
            {
                Id = NewId(),
                BoardId = boardId,
                Name = name,
                Position = "V",
            };
        }

        public static TaskItem Task(string columnId, string title = "Task", string body = "", List<string> noteIds = null)
        {
            return new TaskItem
            {
                Id = NewId(),
                ColumnId = columnId,
                Title = title,
                Body = body,
                Position = "V",
                NoteIds = noteIds ?? new List<string>(),
            };
        }

        public static Attachment Attachment(string noteId = null, string taskId = null, string filename = "file.png")
        {
            return new Attachment
            {
                Id = NewId(),
                NoteId = noteId,
                TaskId = taskId,
                Filename = filename,
                MimeType = "image/png",
                SizeBytes = 3,
            };
        }
    }
}
