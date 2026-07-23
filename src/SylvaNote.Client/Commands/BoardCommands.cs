using Galdr.Native;
using SylvaNote.Client.Commands.Contracts;
using SylvaNote.Client.Services.Abstractions;

namespace SylvaNote.Client.Commands;

internal static class BoardCommands
{
    public static GaldrBuilder AddBoardCommands(this GaldrBuilder builder)
    {
        builder.AddFunction("getBoards", (IBoardService boards) => boards.GetBoards());
        builder.AddFunction("createBoard", (CreateBoardRequest request, IBoardService boards) => boards.CreateBoard(request));
        builder.AddFunction("renameBoard", (RenameBoardRequest request, IBoardService boards) => boards.RenameBoard(request));
        builder.AddFunction("moveBoard", (MoveBoardRequest request, IBoardService boards) => boards.MoveBoard(request));
        builder.AddFunction("deleteBoard", (DeleteBoardRequest request, IBoardService boards) => boards.DeleteBoard(request));
        builder.AddFunction("getBoard", (GetBoardRequest request, IBoardService boards) => boards.GetBoard(request));
        builder.AddFunction("createColumn", (CreateColumnRequest request, IBoardService boards) => boards.CreateColumn(request));
        builder.AddFunction("renameColumn", (RenameColumnRequest request, IBoardService boards) => boards.RenameColumn(request));
        builder.AddFunction("moveColumn", (MoveColumnRequest request, IBoardService boards) => boards.MoveColumn(request));
        builder.AddFunction("deleteColumn", (DeleteColumnRequest request, IBoardService boards) => boards.DeleteColumn(request));
        builder.AddFunction("createTask", (CreateTaskRequest request, IBoardService boards) => boards.CreateTask(request));
        builder.AddFunction("getTask", (GetTaskRequest request, IBoardService boards) => boards.GetTask(request));
        builder.AddFunction("saveTask", (SaveTaskRequest request, IBoardService boards) => boards.SaveTask(request));
        builder.AddFunction("moveTask", (MoveTaskRequest request, IBoardService boards) => boards.MoveTask(request));
        builder.AddFunction("deleteTask", (DeleteTaskRequest request, IBoardService boards) => boards.DeleteTask(request));
        builder.AddFunction("searchTasks", (SearchTasksRequest request, IBoardService boards) => boards.SearchTasks(request));
        builder.AddFunction("searchBoards", (SearchBoardsRequest request, IBoardService boards) => boards.SearchBoards(request));
        return builder;
    }
}
