using System;
using System.Text;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Lorestead.Core.Sync;

namespace Lorestead.Core.Mcp
{
    // Manual registration (McpServerTool.Create) because attribute scanning is
    // reflection-based and not AOT-safe (decisions.md). Results are GaldrJson text so
    // the SDK's internal STJ never serializes app DTOs; get_attachment returns real
    // MCP content blocks instead.
    public static class McpToolRegistry
    {
        public static McpServerTool[] CreateTools(McpToolService tools)
        {
            return new McpServerTool[]
            {
                McpServerTool.Create((string query, int limit = 20, string type = "all") =>
                    Run(() => PayloadJson.Serialize(tools.Search(query, limit, type))),
                    Options("search", "Search notes and tasks by full text and boards by name. Returns ids, titles, breadcrumbs, updatedAt, and snippets only - use get_note or get_task for full content. limit caps each category separately; type is all, notes, tasks, or boards.")),

                McpServerTool.Create((string parentId = null, int depth = 0) =>
                    Run(() => PayloadJson.Serialize(tools.ListNoteTree(parentId, depth))),
                    Options("list_note_tree", "The note tree as ids, titles, updatedAt, and hierarchy only. Trashed notes and templates are excluded. parentId returns just that note's children (and their descendants); depth caps how many levels come back, 0 for all.")),

                McpServerTool.Create((int limit = 20, string type = "all") =>
                    Run(() => PayloadJson.Serialize(tools.ListRecent(limit, type))),
                    Options("list_recent", "Most recently updated notes and tasks as one list, newest first. type is all, notes, or tasks. Use this to see what changed without needing a search query.")),

                McpServerTool.Create((string noteId) =>
                    Run(() => PayloadJson.Serialize(tools.GetNote(noteId))),
                    Options("get_note", "A note's full body and metadata, plus its attachment list and backlinks. A backlink's via says how the source reaches this note: body (its markdown links here), link (a task carrying it in its linked-notes list), or both.")),

                McpServerTool.Create((string title, string body = null, string parentId = null) =>
                    RunAsync(async () => PayloadJson.Serialize(await tools.CreateNote(title, body, parentId))),
                    Options("create_note", "Create a note under parentId, or at the root when parentId is omitted.")),

                McpServerTool.Create((string noteId, string title = null, string body = null) =>
                    RunAsync(async () => PayloadJson.Serialize(await tools.UpdateNote(noteId, title, body))),
                    Options("update_note", "Replace a note's title and/or body. Omitted fields are kept. Prefer append_to_note for additions - a full body replace overwrites concurrent edits.")),

                McpServerTool.Create((string noteId, string markdown) =>
                    RunAsync(async () => PayloadJson.Serialize(await tools.AppendToNote(noteId, markdown))),
                    Options("append_to_note", "Append markdown to the end of a note's body. The safe everyday write - never overwrites existing content.")),

                McpServerTool.Create(() =>
                    Run(() => PayloadJson.Serialize(tools.ListBoards())),
                    Options("list_boards", "All boards with their columns as ids and names.")),

                McpServerTool.Create((string boardId) =>
                    Run(() => PayloadJson.Serialize(tools.GetBoard(boardId))),
                    Options("get_board", "A board's columns, each with its tasks as ids and titles. Use get_task for a task's content.")),

                McpServerTool.Create((string taskId) =>
                    Run(() => PayloadJson.Serialize(tools.GetTask(taskId))),
                    Options("get_task", "A task's full body and metadata (including its board and column names, not just ids), plus its attachment list and linked notes.")),

                McpServerTool.Create((string columnId, string title, string body = null, string[] noteIds = null) =>
                    RunAsync(async () => PayloadJson.Serialize(await tools.CreateTask(columnId, title, body, noteIds))),
                    Options("create_task", "Create a task in a column. noteIds is an optional list of note ids to link.")),

                McpServerTool.Create((string taskId, string title = null, string body = null) =>
                    RunAsync(async () => PayloadJson.Serialize(await tools.UpdateTask(taskId, title, body))),
                    Options("update_task", "Replace a task's title and/or body. Omitted fields are kept.")),

                McpServerTool.Create((string taskId, string columnId, int index = -1) =>
                    RunAsync(async () => PayloadJson.Serialize(await tools.MoveTask(taskId, columnId, index))),
                    Options("move_task", "Move a task to a column at a zero-based index among its tasks. Omit index to place it last. Returns the column and the index it actually landed at, which differs from the request when the index is clamped.")),

                McpServerTool.Create((string taskId, string noteId) =>
                    RunAsync(async () => PayloadJson.Serialize(await tools.LinkNoteToTask(taskId, noteId))),
                    Options("link_note_to_task", "Link a note to an existing task. Additive - already-linked notes are a no-op. Unlinking is done by the user in the app.")),

                McpServerTool.Create(() =>
                    Run(() => PayloadJson.Serialize(tools.ListTemplates())),
                    Options("list_templates", "Template roots as ids and titles.")),

                McpServerTool.Create((string title, string body) =>
                    RunAsync(async () => PayloadJson.Serialize(await tools.CreateTemplate(title, body))),
                    Options("create_template", "Create a new template from the supplied content.")),

                McpServerTool.Create((string templateId, string title, string parentId = null) =>
                    RunAsync(async () => PayloadJson.Serialize(await tools.CreateNoteFromTemplate(templateId, title, parentId))),
                    Options("create_note_from_template", "Instantiate a template (with its subtree) as a new note under parentId, or at the root when parentId is omitted.")),

                McpServerTool.Create((string attachmentId) =>
                    AttachmentResult(tools, attachmentId),
                    Options("get_attachment", "An attachment's content, returned as the MCP content type matching its mime type.")),

                McpServerTool.Create((string filename, string mimeType, string dataBase64, string noteId = null, string taskId = null) =>
                    RunAsync(async () => PayloadJson.Serialize(await tools.AddAttachment(filename, mimeType, dataBase64, noteId, taskId))),
                    Options("add_attachment", "Attach base64-encoded content to a note or a task (exactly one of noteId/taskId). 100 MB limit.")),
            };
        }

        private static McpServerToolCreateOptions Options(string name, string description)
        {
            return new McpServerToolCreateOptions { Name = name, Description = description };
        }

        private static CallToolResult Run(Func<string> action)
        {
            CallToolResult result = new CallToolResult();
            try
            {
                result.Content.Add(new TextContentBlock { Text = action() });
            }
            catch (Exception ex)
            {
                // Failures must reach the agent as isError tool results with the real
                // message, not opaque protocol errors.
                result.IsError = true;
                result.Content.Add(new TextContentBlock { Text = ex.Message });
            }
            return result;
        }

        private static async Task<CallToolResult> RunAsync(Func<Task<string>> action)
        {
            CallToolResult result = new CallToolResult();
            try
            {
                result.Content.Add(new TextContentBlock { Text = await action() });
            }
            catch (Exception ex)
            {
                // Same contract as Run.
                result.IsError = true;
                result.Content.Add(new TextContentBlock { Text = ex.Message });
            }
            return result;
        }

        private static CallToolResult AttachmentResult(McpToolService tools, string attachmentId)
        {
            CallToolResult result = new CallToolResult();
            try
            {
                result.Content.Add(ToContentBlock(tools.GetAttachment(attachmentId)));
            }
            catch (Exception ex)
            {
                // Same contract as Run.
                result.IsError = true;
                result.Content.Add(new TextContentBlock { Text = ex.Message });
            }
            return result;
        }

        private static ContentBlock ToContentBlock(McpAttachmentData attachment)
        {
            ContentBlock block;
            string mime = string.IsNullOrEmpty(attachment.MimeType) ? "application/octet-stream" : attachment.MimeType;

            if (mime.StartsWith("image/", StringComparison.Ordinal))
            {
                block = new ImageContentBlock { MimeType = mime, Data = Base64Bytes(attachment.Data) };
            }
            else if (mime.StartsWith("text/", StringComparison.Ordinal))
            {
                block = new TextContentBlock { Text = Encoding.UTF8.GetString(attachment.Data) };
            }
            else
            {
                block = new EmbeddedResourceBlock
                {
                    Resource = new BlobResourceContents
                    {
                        Uri = "attachment://" + attachment.Id,
                        MimeType = mime,
                        Blob = Base64Bytes(attachment.Data),
                    },
                };
            }

            return block;
        }

        // SDK 1.4.x convention: Data/Blob hold the base64 TEXT as UTF-8 bytes, not the
        // raw binary - the polymorphic ContentBlock converter writes the span verbatim
        // as a JSON string, so raw bytes would produce a corrupt non-base64 value.
        private static ReadOnlyMemory<byte> Base64Bytes(byte[] data)
        {
            return Encoding.ASCII.GetBytes(Convert.ToBase64String(data));
        }
    }
}
