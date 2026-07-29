using System;
using Lorestead.Core.Entities;
using Xunit;

namespace Lorestead.IntegrationTests
{
    public sealed class SettingsTests
    {
        [Fact]
        public void DefaultsAreSeededByMigration()
        {
            using TestDb db = new TestDb();
            ApplicationSettings app = db.Settings.GetApplication();
            Assert.NotNull(app);
            Assert.Equal(50, app.HistoryRetention);
            Assert.Equal("system", app.Theme);
            Assert.Equal(30, app.TrashRetentionDays);
            Assert.True(app.AutoCheckUpdates);
            Assert.False(app.AutoUpdate);

            EditorSettings editor = db.Settings.GetEditor();
            Assert.NotNull(editor);
            Assert.Equal(14, editor.FontSize);
            Assert.True(editor.MdTables);
            Assert.True(editor.MdTaskLists);
            Assert.True(editor.MdStrikethrough);
            Assert.True(editor.MdAutolinks);
            Assert.True(editor.MdFootnotes);
            Assert.True(editor.MdCodeHighlighting);
            Assert.True(editor.MdHighlight);
        }

        [Fact]
        public void SettingsRoundTrip()
        {
            using TestDb db = new TestDb();
            ApplicationSettings app = db.Settings.GetApplication();
            app.HistoryRetention = 75;
            app.ServerUrl = "https://sync.example.test";
            app.Theme = "dark";
            app.WindowWidth = 1600;
            db.Settings.SaveApplication(app);

            ApplicationSettings reloaded = db.Settings.GetApplication();
            Assert.Equal(75, reloaded.HistoryRetention);
            Assert.Equal("https://sync.example.test", reloaded.ServerUrl);
            Assert.Equal("dark", reloaded.Theme);
            Assert.Equal(1600, reloaded.WindowWidth);

            EditorSettings editor = db.Settings.GetEditor();
            editor.FontSize = 16;
            editor.MdFootnotes = false;
            db.Settings.SaveEditor(editor);

            EditorSettings reloadedEditor = db.Settings.GetEditor();
            Assert.Equal(16, reloadedEditor.FontSize);
            Assert.False(reloadedEditor.MdFootnotes);
        }

        [Fact]
        public void SyncStateInitializesOnceWithValidDeviceId()
        {
            using TestDb db = new TestDb();
            SyncState first = db.SyncState.EnsureInitialized();
            Assert.True(Guid.TryParse(first.DeviceId, out Guid _));
            Assert.Equal(0, first.LastSeenSeq);

            SyncState second = db.SyncState.EnsureInitialized();
            Assert.Equal(first.DeviceId, second.DeviceId);
        }
    }
}
