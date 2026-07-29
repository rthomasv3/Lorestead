using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Galdr.Native;
using Lorestead.Client.Commands.Contracts;
using Lorestead.Client.Services.Abstractions;
using Lorestead.Core.DataAccess;
using Lorestead.Core.Entities;
using Lorestead.Core.Import;

namespace Lorestead.Client.Services;

public sealed class ImportService : IImportService
{
    private const string NoteExtension = ".md";
    private const string ZipExtension = ".zip";

    private readonly ConnectionManager _connectionManager;
    private readonly RepositoryFactory _repositories;
    private readonly SyncStateRepository _syncState;
    private readonly SettingsRepository _settings;
    private readonly IDialogService _dialogs;
    private readonly ISyncService _sync;

    // The dialog picks a source, previews it, then runs it - the singleton service
    // is what carries the selection between those calls.
    private PendingSource _pending;

    private sealed class PendingSource
    {
        public string Path { get; set; }
        public bool IsZip { get; set; }
        public List<ImportFile> Files { get; set; }
    }

    public ImportService(
        ConnectionManager connectionManager,
        RepositoryFactory repositories,
        SyncStateRepository syncState,
        SettingsRepository settings,
        IDialogService dialogs,
        ISyncService sync)
    {
        _connectionManager = connectionManager;
        _repositories = repositories;
        _syncState = syncState;
        _settings = settings;
        _dialogs = dialogs;
        _sync = sync;
    }

    public ImportPreflightResponse PickFile(PreviewImportRequest request)
    {
        ImportPreflightResponse response = new ImportPreflightResponse();
        string path = _dialogs.OpenFileDialog(filterList: "zip,md");

        if (!string.IsNullOrEmpty(path))
        {
            bool isZip = path.EndsWith(ZipExtension, StringComparison.OrdinalIgnoreCase);
            _pending = new PendingSource
            {
                Path = path,
                IsZip = isZip,
                Files = isZip ? ReadZip(path) : ReadSingleNote(path),
            };
            response = Preflight(request.DestinationParentId);
        }

        return response;
    }

    public ImportPreflightResponse PickFolder(PreviewImportRequest request)
    {
        ImportPreflightResponse response = new ImportPreflightResponse();
        string path = _dialogs.OpenDirectoryDialog();

        if (!string.IsNullOrEmpty(path))
        {
            _pending = new PendingSource
            {
                Path = path,
                IsZip = false,
                Files = ReadFolder(path),
            };
            response = Preflight(request.DestinationParentId);
        }

        return response;
    }

    public ImportPreflightResponse Preview(PreviewImportRequest request)
    {
        ImportPreflightResponse response = new ImportPreflightResponse();
        if (_pending != null)
        {
            response = Preflight(request.DestinationParentId);
        }
        return response;
    }

    public RunImportResponse Run(RunImportRequest request)
    {
        PendingSource pending = _pending;
        if (pending == null)
        {
            throw new InvalidOperationException("No import source is selected.");
        }

        // Rebuilt rather than reused from the preflight: the destination is chosen
        // after the source, and the database may have moved underneath.
        ImportPlan plan = BuildPlan(pending, request.DestinationParentId);

        string deviceId = _syncState.Get().DeviceId;
        int historyRetention = _settings.GetApplication().HistoryRetention;

        if (pending.IsZip)
        {
            using ZipArchive archive = ZipFile.OpenRead(pending.Path);
            Dictionary<string, ZipArchiveEntry> entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                entries[NormalizePath(entry.FullName)] = entry;
            }
            ImportApplier.Apply(_connectionManager, deviceId, historyRetention, plan,
                sourcePath => ReadZipEntry(entries, sourcePath));
        }
        else
        {
            string root = File.Exists(pending.Path) ? Path.GetDirectoryName(pending.Path) : pending.Path;
            ImportApplier.Apply(_connectionManager, deviceId, historyRetention, plan,
                sourcePath => File.ReadAllBytes(Path.Combine(root, sourcePath)));
        }

        _pending = null;
        _sync.NotifyLocalChange();
        return BuildReport(plan);
    }

    private ImportPreflightResponse Preflight(string destinationParentId)
    {
        ImportPlan plan = BuildPlan(_pending, destinationParentId);
        ImportPreflightResponse response = new ImportPreflightResponse
        {
            Selected = true,
            Path = _pending.Path,
            NoteCount = plan.Notes.Count,
            AttachmentCount = plan.Attachments.Count,
        };

        foreach (ImportedNote note in plan.Notes)
        {
            if (note.Action == ImportAction.Create)
            {
                response.CreatedCount++;
            }
            else if (note.Action == ImportAction.Merge)
            {
                response.MergedCount++;
            }
            else
            {
                response.SkippedCount++;
            }
            if (note.Type == NoteType.Template)
            {
                response.TemplateCount++;
            }
        }

        return response;
    }

    private ImportPlan BuildPlan(PendingSource pending, string destinationParentId)
    {
        return MarkdownImportBuilder.Build(new ImportSource
        {
            Files = pending.Files,
            ExistingNotes = _repositories.Notes.GetAll(),
            ExistingAttachments = _repositories.Attachments.GetAllForNotes(),
            DestinationParentId = destinationParentId,
        });
    }

    private static RunImportResponse BuildReport(ImportPlan plan)
    {
        RunImportResponse response = new RunImportResponse
        {
            AttachmentCount = plan.Attachments.Count,
            Warnings = plan.Warnings,
        };

        foreach (ImportedNote note in plan.Notes)
        {
            if (note.Action == ImportAction.Create)
            {
                response.Created++;
            }
            else if (note.Action == ImportAction.Merge)
            {
                response.Merged++;
            }
            else
            {
                response.Skipped++;
            }
            if (note.Type == NoteType.Template)
            {
                response.TemplateCount++;
            }
        }

        return response;
    }

    private static List<ImportFile> ReadZip(string path)
    {
        List<ImportFile> files = new List<ImportFile>();
        using ZipArchive archive = ZipFile.OpenRead(path);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string normalized = NormalizePath(entry.FullName);
            if (normalized.Length > 0 && !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                ImportFile file = new ImportFile { Path = normalized, SizeBytes = entry.Length };
                if (normalized.EndsWith(NoteExtension, StringComparison.OrdinalIgnoreCase))
                {
                    using StreamReader reader = new StreamReader(entry.Open());
                    file.Content = reader.ReadToEnd();
                }
                files.Add(file);
            }
        }

        return files;
    }

    private static List<ImportFile> ReadFolder(string root)
    {
        List<ImportFile> files = new List<ImportFile>();

        foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            string relative = NormalizePath(Path.GetRelativePath(root, path));
            ImportFile file = new ImportFile
            {
                Path = relative,
                SizeBytes = new FileInfo(path).Length,
            };
            if (relative.EndsWith(NoteExtension, StringComparison.OrdinalIgnoreCase))
            {
                file.Content = File.ReadAllText(path);
            }
            files.Add(file);
        }

        return files;
    }

    private static List<ImportFile> ReadSingleNote(string path)
    {
        return new List<ImportFile>
        {
            new ImportFile
            {
                Path = Path.GetFileName(path),
                Content = File.ReadAllText(path),
                SizeBytes = new FileInfo(path).Length,
            },
        };
    }

    private static byte[] ReadZipEntry(Dictionary<string, ZipArchiveEntry> entries, string sourcePath)
    {
        byte[] result = new byte[0];
        ZipArchiveEntry entry;

        if (entries.TryGetValue(sourcePath, out entry))
        {
            using Stream stream = entry.Open();
            using MemoryStream buffer = new MemoryStream();
            stream.CopyTo(buffer);
            result = buffer.ToArray();
        }

        return result;
    }

    private static string NormalizePath(string path)
    {
        return (path ?? string.Empty).Replace('\\', '/').Trim('/');
    }
}
