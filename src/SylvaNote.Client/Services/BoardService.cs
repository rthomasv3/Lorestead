using System;
using System.Collections.Generic;
using SylvaNote.Client.Commands.Contracts;
using SylvaNote.Client.Services.Abstractions;
using SylvaNote.Core.DataAccess;
using SylvaNote.Core.Entities;
using SylvaNote.Core.Ordering;

namespace SylvaNote.Client.Services;

public sealed class BoardService : IBoardService
{
    private readonly RepositoryFactory _repositories;
    private readonly ISyncService _sync;

    public BoardService(RepositoryFactory repositories, ISyncService sync)
    {
        _repositories = repositories;
        _sync = sync;
    }

    public GetBoardsResponse GetBoards()
    {
        return new GetBoardsResponse { Boards = _repositories.Boards.GetActive() };
    }

    public CreateBoardResponse CreateBoard(CreateBoardRequest request)
    {
        BoardRepository boards = _repositories.Boards;
        Board board = new Board
        {
            Id = Guid.CreateVersion7().ToString(),
            Name = request.Name ?? string.Empty,
            Position = FractionalIndex.Between(boards.GetMaxPosition(), null),
        };
        boards.Save(board);
        _sync.NotifyLocalChange();
        return new CreateBoardResponse { Board = board };
    }

    public RenameBoardResponse RenameBoard(RenameBoardRequest request)
    {
        BoardRepository boards = _repositories.Boards;
        Board board = GetRequiredBoard(boards, request.Id);
        board.Name = request.Name ?? string.Empty;
        boards.Save(board);
        _sync.NotifyLocalChange();
        return new RenameBoardResponse { UpdatedAt = board.UpdatedAt };
    }

    public MoveBoardResponse MoveBoard(MoveBoardRequest request)
    {
        BoardRepository boards = _repositories.Boards;
        Board board = GetRequiredBoard(boards, request.Id);
        string lower = request.PreviousId != null ? boards.Get(request.PreviousId)?.Position : null;
        string upper = request.NextId != null ? boards.Get(request.NextId)?.Position : null;
        board.Position = AllocatePosition(lower, upper, position => boards.PositionExists(position));
        boards.Save(board);
        _sync.NotifyLocalChange();
        return new MoveBoardResponse { Position = board.Position };
    }

    public DeleteBoardResponse DeleteBoard(DeleteBoardRequest request)
    {
        _repositories.Boards.DeleteCascade(request.Id);
        _sync.NotifyLocalChange();
        return new DeleteBoardResponse { Ok = true };
    }

    public GetBoardResponse GetBoard(GetBoardRequest request)
    {
        List<BoardColumn> columns = _repositories.Columns.GetActiveForBoard(request.Id);
        Dictionary<string, int> counts = _repositories.Attachments.CountByTaskForBoard(request.Id);
        Dictionary<string, int> linkCounts = _repositories.Tasks.CountNoteLinksForBoard(request.Id);
        List<TaskSummary> tasks = new List<TaskSummary>();
        foreach (TaskItem task in _repositories.Tasks.GetActiveForBoard(request.Id))
        {
            tasks.Add(new TaskSummary
            {
                Id = task.Id,
                ColumnId = task.ColumnId,
                Title = task.Title,
                Body = task.Body,
                Position = task.Position,
                AttachmentCount = counts.TryGetValue(task.Id, out int count) ? count : 0,
                LinkedNoteCount = linkCounts.TryGetValue(task.Id, out int linkCount) ? linkCount : 0,
                CreatedAt = task.CreatedAt,
                UpdatedAt = task.UpdatedAt,
            });
        }
        return new GetBoardResponse { Columns = columns, Tasks = tasks };
    }

    public CreateColumnResponse CreateColumn(CreateColumnRequest request)
    {
        BoardColumnRepository columns = _repositories.Columns;
        BoardColumn column = new BoardColumn
        {
            Id = Guid.CreateVersion7().ToString(),
            BoardId = request.BoardId,
            Name = request.Name ?? string.Empty,
            Position = FractionalIndex.Between(columns.GetMaxPosition(request.BoardId), null),
        };
        columns.Save(column);
        _sync.NotifyLocalChange();
        return new CreateColumnResponse { Column = column };
    }

    public RenameColumnResponse RenameColumn(RenameColumnRequest request)
    {
        BoardColumnRepository columns = _repositories.Columns;
        BoardColumn column = GetRequiredColumn(columns, request.Id);
        column.Name = request.Name ?? string.Empty;
        columns.Save(column);
        _sync.NotifyLocalChange();
        return new RenameColumnResponse { UpdatedAt = column.UpdatedAt };
    }

    public MoveColumnResponse MoveColumn(MoveColumnRequest request)
    {
        BoardColumnRepository columns = _repositories.Columns;
        BoardColumn column = GetRequiredColumn(columns, request.Id);
        string lower = request.PreviousId != null ? columns.Get(request.PreviousId)?.Position : null;
        string upper = request.NextId != null ? columns.Get(request.NextId)?.Position : null;
        column.Position = AllocatePosition(lower, upper, position => columns.PositionExists(column.BoardId, position));
        columns.Save(column);
        _sync.NotifyLocalChange();
        return new MoveColumnResponse { Position = column.Position };
    }

    public DeleteColumnResponse DeleteColumn(DeleteColumnRequest request)
    {
        _repositories.Columns.DeleteCascade(request.Id);
        _sync.NotifyLocalChange();
        return new DeleteColumnResponse { Ok = true };
    }

    public CreateTaskResponse CreateTask(CreateTaskRequest request)
    {
        TaskRepository tasks = _repositories.Tasks;
        TaskItem task = new TaskItem
        {
            Id = Guid.CreateVersion7().ToString(),
            ColumnId = request.ColumnId,
            Title = request.Title ?? string.Empty,
            Body = string.Empty,
            Position = FractionalIndex.Between(tasks.GetMaxPosition(request.ColumnId), null),
            NoteIds = new List<string>(),
        };
        tasks.Save(task);
        _sync.NotifyLocalChange();
        return new CreateTaskResponse { Task = task };
    }

    public GetTaskResponse GetTask(GetTaskRequest request)
    {
        return new GetTaskResponse
        {
            Task = _repositories.Tasks.Get(request.Id),
            Attachments = _repositories.Attachments.GetForTask(request.Id),
        };
    }

    public SaveTaskResponse SaveTask(SaveTaskRequest request)
    {
        TaskRepository tasks = _repositories.Tasks;
        TaskItem task = GetRequiredTask(tasks, request.Id);
        task.Title = request.Title ?? string.Empty;
        task.Body = request.Body ?? string.Empty;
        task.NoteIds = request.NoteIds ?? new List<string>();
        tasks.Save(task);
        _sync.NotifyLocalChange();
        return new SaveTaskResponse { UpdatedAt = task.UpdatedAt };
    }

    public MoveTaskResponse MoveTask(MoveTaskRequest request)
    {
        TaskRepository tasks = _repositories.Tasks;
        TaskItem task = GetRequiredTask(tasks, request.Id);
        task.ColumnId = request.ColumnId;
        string lower = request.PreviousId != null ? tasks.Get(request.PreviousId)?.Position : null;
        string upper = request.NextId != null ? tasks.Get(request.NextId)?.Position : null;
        task.Position = AllocatePosition(lower, upper, position => tasks.PositionExists(request.ColumnId, position));
        tasks.Save(task);
        _sync.NotifyLocalChange();
        return new MoveTaskResponse { Position = task.Position };
    }

    public DeleteTaskResponse DeleteTask(DeleteTaskRequest request)
    {
        _repositories.Tasks.Delete(request.Id);
        _sync.NotifyLocalChange();
        return new DeleteTaskResponse { Ok = true };
    }

    public SearchTasksResponse SearchTasks(SearchTasksRequest request)
    {
        return new SearchTasksResponse { Results = _repositories.Search.SearchTasksWithContext(request.Query) };
    }

    public SearchBoardsResponse SearchBoards(SearchBoardsRequest request)
    {
        return new SearchBoardsResponse { Results = _repositories.Search.SearchBoards(request.Query) };
    }

    private static Board GetRequiredBoard(BoardRepository boards, string id)
    {
        Board board = boards.Get(id);
        if (board == null)
        {
            throw new InvalidOperationException($"Board '{id}' does not exist.");
        }
        return board;
    }

    private static BoardColumn GetRequiredColumn(BoardColumnRepository columns, string id)
    {
        BoardColumn column = columns.Get(id);
        if (column == null)
        {
            throw new InvalidOperationException($"Column '{id}' does not exist.");
        }
        return column;
    }

    private static TaskItem GetRequiredTask(TaskRepository tasks, string id)
    {
        TaskItem task = tasks.Get(id);
        if (task == null)
        {
            throw new InvalidOperationException($"Task '{id}' does not exist.");
        }
        return task;
    }

    // Same neighbor-derived allocation as notes: stale bounds fall back to
    // "after previous", and the loop dodges keys held by tombstoned siblings.
    private static string AllocatePosition(string lower, string upper, Func<string, bool> exists)
    {
        if (lower != null && upper != null && string.CompareOrdinal(lower, upper) >= 0)
        {
            upper = null;
        }

        string position = FractionalIndex.Between(lower, upper);
        while (exists(position))
        {
            position = FractionalIndex.Between(position, upper);
        }
        return position;
    }
}
