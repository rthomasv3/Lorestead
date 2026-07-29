using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Galdr.Native;
using Microsoft.Extensions.DependencyInjection;
using Lorestead.Client.Commands;
using Lorestead.Client.Services;
using Lorestead.Client.Services.Abstractions;
using Lorestead.Core.DataAccess;
using Lorestead.Core.DataAccess.Migrations;
using Lorestead.Core.Entities;
using Lorestead.Core.FirstRun;

namespace Lorestead.Client;

internal class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Explicit provider init: the reflection-based auto-init is not AOT-reliable,
        // and this host's provider is plain SQLite (bundle_e_sqlite3).
        SQLitePCL.Batteries_V2.Init();

        Config config = Config.Create();
        FileLoggingService logger = new FileLoggingService(config);
        ConnectionManager connectionManager = new ConnectionManager();

        GaldrBuilder builder = new GaldrBuilder()
            .SetTitle("Lorestead")
            .SetSize(1200, 800)
            .SetMinSize(800, 600)
            .UseSingleInstance("lorestead")
            .EnableSpellChecking("en_US", "en_GB")
            .AddSingleton(config)
            .AddSingleton<ILoggingService>(logger)
            .AddSingleton(connectionManager)
            .AddSingleton<SettingsRepository>()
            .AddSingleton<SyncStateRepository>()
            .AddSingleton<RepositoryFactory>()
            .AddSingleton<SyncCredentialStore>()
            .AddSingleton<ISettingsService, SettingsService>()
            .AddSingleton<INoteService, NoteService>()
            .AddSingleton<IAttachmentService, AttachmentService>()
            .AddSingleton<IExportService, ExportService>()
            .AddSingleton<IImportService, ImportService>()
            .AddSingleton<IBoardService, BoardService>()
            .AddSingleton<ISyncService, SyncEngine>()
            .AddSingleton<IChangeWatcher, DataVersionWatcher>()
            .OnBeforeStartup(() =>
            {
                // Startup survives a broken DB so Settings (and its Logs section) still
                // loads - commands that need the DB then fail into the log instead.
                try
                {
                    bool created = connectionManager.Open(config.DbFilePath, MigrationSets.Client());
                    SyncState syncState = new SyncStateRepository(connectionManager).EnsureInitialized();
                    if (created)
                    {
                        FirstRunSeeder.Seed(connectionManager, syncState.DeviceId);
                    }
                    PurgeExpiredTrash(connectionManager, syncState.DeviceId);
                }
                catch (Exception ex)
                {
                    logger.Error("Startup", "Database open/migration failed", ex);
                }
            })
            .OnStartup(serviceProvider =>
            {
                RestoreWindow(serviceProvider, logger);
                serviceProvider.GetRequiredService<ISyncService>().Start();
                serviceProvider.GetRequiredService<IChangeWatcher>().Start();
            })
            .OnWindowChanged((galdr, context, serviceProvider) => SaveWindow(context, serviceProvider, logger))
            .OnCommandError((context, serviceProvider) =>
            {
                ILoggingService log = serviceProvider.GetService<ILoggingService>();

                if (log != null)
                {
                    string source = string.IsNullOrEmpty(context.CommandName) ? "galdrInvoke" : context.CommandName;
                    log.Error(source, "Command handler threw", context.Exception);
                }
            })
            .OnUnhandledException((context, serviceProvider) =>
            {
                ILoggingService log = serviceProvider?.GetService<ILoggingService>();

                if (log != null)
                {
                    string source = context.Source == UnhandledExceptionSource.AppDomain ? "AppDomain" : "TaskScheduler";
                    string message = context.IsTerminating
                        ? "Unhandled exception - process is terminating"
                        : "Unhandled exception (non-terminating)";

                    log.Error(source, message, context.Exception);
                }
            });

        builder.AddSettingsCommands();
        builder.AddSyncCommands();
        builder.AddSystemCommands();
        builder.AddNoteCommands();
        builder.AddAttachmentCommands();
        builder.AddBoardCommands();
        builder.AddExportCommands();
        builder.AddImportCommands();

//-:cnd:noEmit
#if DEBUG
        builder.SetDebug(true)
               .SetContentProvider(new UrlContent("http://localhost:5174"));
#else
        string wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        builder.SetContentProvider(new FolderContent(wwwroot, hostname: "lorestead.localhost"));
#endif
        //+:cnd:noEmit

        using Galdr.Native.Galdr galdr = builder.Build().Run();
    }

    static void PurgeExpiredTrash(ConnectionManager connectionManager, string deviceId)
    {
        ApplicationSettings settings = new SettingsRepository(connectionManager).GetApplication();
        string cutoff = DateTime.UtcNow
            .AddDays(-settings.TrashRetentionDays)
            .ToString("O", CultureInfo.InvariantCulture);
        new NoteRepository(connectionManager, deviceId).PurgeExpiredTrash(cutoff);
    }

    static void RestoreWindow(IServiceProvider serviceProvider, ILoggingService logger)
    {
        try
        {
            ISettingsService settings = serviceProvider.GetRequiredService<ISettingsService>();
            Galdr.Native.Galdr galdr = serviceProvider.GetRequiredService<Galdr.Native.Galdr>();
            WindowData windowData = settings.GetWindowData();

            // Skip size restoration on macOS - AppKit's setFrameAutosaveName (wired in
            // Galdr.Native) persists frame natively; state is not part of that autosave,
            // so Maximized / Fullscreen still applies there.
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && windowData.Width > 0 && windowData.Height > 0)
            {
                galdr.SetSize(windowData.Width, windowData.Height, WebviewHint.None);
            }

            if (windowData.State == "maximized")
            {
                galdr.SetWindowState(WindowState.Maximized);
            }
            else if (windowData.State == "fullscreen")
            {
                galdr.SetWindowState(WindowState.Fullscreen);
            }
        }
        catch (Exception ex)
        {
            logger.Error("Startup", "Window restore failed", ex);
        }
    }

    static void SaveWindow(WindowChangedContext context, IServiceProvider serviceProvider, ILoggingService logger)
    {
        try
        {
            if (context.State != WindowState.Minimized)
            {
                ISettingsService settings = serviceProvider.GetRequiredService<ISettingsService>();

                // Size only persists in the Normal state - saving while maximized or
                // fullscreen would record screen-sized dimensions as the windowed size.
                if (context.State == WindowState.Normal)
                {
                    settings.SaveWindowSize(context.Width, context.Height);
                }
                else
                {
                    settings.SaveWindowState(context.State == WindowState.Fullscreen ? "fullscreen" : "maximized");
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error("WindowChanged", "Window persistence failed", ex);
        }
    }
}
