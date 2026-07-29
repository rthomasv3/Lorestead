using Lorestead.Client.Commands.Contracts;

namespace Lorestead.Client.Services.Abstractions;

public interface IBoardService
{
    GetBoardsResponse GetBoards();
    CreateBoardResponse CreateBoard(CreateBoardRequest request);
    RenameBoardResponse RenameBoard(RenameBoardRequest request);
    MoveBoardResponse MoveBoard(MoveBoardRequest request);
    DeleteBoardResponse DeleteBoard(DeleteBoardRequest request);
    GetBoardResponse GetBoard(GetBoardRequest request);
    CreateColumnResponse CreateColumn(CreateColumnRequest request);
    RenameColumnResponse RenameColumn(RenameColumnRequest request);
    MoveColumnResponse MoveColumn(MoveColumnRequest request);
    DeleteColumnResponse DeleteColumn(DeleteColumnRequest request);
    CreateTaskResponse CreateTask(CreateTaskRequest request);
    GetTaskResponse GetTask(GetTaskRequest request);
    SaveTaskResponse SaveTask(SaveTaskRequest request);
    MoveTaskResponse MoveTask(MoveTaskRequest request);
    DeleteTaskResponse DeleteTask(DeleteTaskRequest request);
    SearchTasksResponse SearchTasks(SearchTasksRequest request);
    SearchBoardsResponse SearchBoards(SearchBoardsRequest request);
}
