using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证日志面板的旧布局迁移、菜单恢复和会话生命周期。
/// </summary>
public class LogLayoutIntegrationTests
{
    /// <summary>
    /// 旧主布局、自定义预设与浮窗保持原样，新增日志只加入隐藏集合。
    /// </summary>
    [Fact]
    public void VersionOnePreservesCurrentAndNamedLayoutsWhileAddingHiddenLog()
    {
        var layout = CreateLegacyLayout();
        var legacy = new WorkspaceLayoutFile
        {
            Version = 1,
            CurrentPresetId = "custom",
            CurrentLayout = layout,
            UserPresets = [new() { Id = "custom", Name = "保留的布局", Layout = layout }]
        };
        var migrated = WorkspaceLayoutSerializer.Deserialize(JsonSerializer.Serialize(legacy));

        Assert.Equal(WorkspaceLayoutFile.CURRENT_VERSION, migrated.Version);
        Assert.Equal(legacy.CurrentPresetId, migrated.CurrentPresetId);
        Assert.Equal(JsonSerializer.Serialize(layout.Main), JsonSerializer.Serialize(migrated.CurrentLayout.Main));
        Assert.Equal(JsonSerializer.Serialize(layout.FloatingWindows),
            JsonSerializer.Serialize(migrated.CurrentLayout.FloatingWindows));
        Assert.Equal(layout.FocusedPanelId, migrated.CurrentLayout.FocusedPanelId);
        Assert.Equal(WorkbenchPanelIds.All.Except(WorkbenchPanelIds.ForVersion(1)), migrated.CurrentLayout.HiddenPanelIds);
        var preset = Assert.Single(migrated.UserPresets);
        Assert.Equal("保留的布局", preset.Name);
        Assert.Equal(WorkbenchPanelIds.All.Except(WorkbenchPanelIds.ForVersion(1)), preset.Layout.HiddenPanelIds);
        Assert.Equal(JsonSerializer.Serialize(layout.Main), JsonSerializer.Serialize(preset.Layout.Main));
        Assert.True(WorkspaceLayoutValidator.TryValidate(migrated, out var error), error);
    }

    /// <summary>
    /// 当前版本缺日志或旧版本有损坏面板时仍拒绝，迁移不能掩盖损坏。
    /// </summary>
    [Theory]
    [InlineData(1, "missing")]
    [InlineData(1, "duplicate")]
    [InlineData(2, "missing")]
    public void MigrationDoesNotRepairInvalidMembership(int version, string invalid)
    {
        var layout = CreateLegacyLayout();

        if (invalid == "duplicate")
        {
            layout.Main!.PanelIds.Add(WorkbenchPanelIds.COMPOSER);
        }
        else if (version == 1)
        {
            layout.Main!.PanelIds.Remove(WorkbenchPanelIds.COMPOSER);
        }

        var file = new WorkspaceLayoutFile { Version = version, CurrentLayout = layout };
        Assert.Throws<InvalidDataException>(() => WorkspaceLayoutSerializer.Deserialize(JsonSerializer.Serialize(file)));
    }

    /// <summary>
    /// 主文件损坏时，旧版本备份也通过同一迁移入口恢复日志面板。
    /// </summary>
    [Fact]
    public async Task LegacyBackupRestoresWithoutDiscardingExistingGeometry()
    {
        var directory = Path.Combine(Path.GetTempPath(), "danmu-log-layout", Guid.NewGuid().ToString("N"));

        try
        {
            var store = new WorkspaceLayoutStore(directory);
            Directory.CreateDirectory(store.BackupDirectory);
            var legacy = new WorkspaceLayoutFile { Version = 1, CurrentLayout = CreateLegacyLayout() };
            await File.WriteAllTextAsync(store.FilePath, "invalid json");
            await File.WriteAllTextAsync(Path.Combine(store.BackupDirectory, "layouts.json.legacy.bak"),
                JsonSerializer.Serialize(legacy));
            var recovered = await store.LoadAsync();

            Assert.Equal(WorkspaceLayoutFile.CURRENT_VERSION, recovered.Version);
            Assert.Equal(WorkbenchPanelIds.All.Except(WorkbenchPanelIds.ForVersion(1)), recovered.CurrentLayout.HiddenPanelIds);
            Assert.Equal(-1200, Assert.Single(recovered.CurrentLayout.FloatingWindows).Bounds.X);
            Assert.Single(Directory.GetFiles(store.CorruptDirectory));
            var saved = WorkspaceLayoutSerializer.Deserialize(await File.ReadAllTextAsync(store.FilePath));
            Assert.Equal(WorkspaceLayoutFile.CURRENT_VERSION, saved.Version);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// 关闭的日志保留历史和未读，视图菜单恢复后可读取，浮动重开仍复用原实例。
    /// </summary>
    [AvaloniaFact]
    public async Task HiddenAndFloatingLogRetainsSessionAndUpdatesUnreadMenu()
    {
        using var fixture = new WorkspaceSidebarTestContext(240);
        fixture.ViewModel.Settings.UseWindowMenuOnMac = true;
        var main = new MainWindow { DataContext = fixture.ViewModel };

        try
        {
            main.Show();
            main.Activate();
            await main.Initialization;
            var session = Assert.IsType<WorkbenchSession>(main.Workbench);
            var adapter = session.Layouts.PanelAdapters[WorkbenchPanelIds.LOG];
            var originalView = adapter.View;
            session.Layouts.HidePanel(WorkbenchPanelIds.LOG);
            fixture.ViewModel.Log.Error("离线测试", "待查看的错误");
            Pump(main);
            Assert.Equal("日志 (1)", adapter.Title);
            var entry = session.MenuCatalog.Entries.Single(group => group.Id == "views").Children!
                .Single(item => item.Id == $"panel:{WorkbenchPanelIds.LOG}");
            Assert.Equal("日志 (1)", entry.Header);
            var active = session.Layouts.Capture().FocusedPanelId;
            Assert.NotEqual(WorkbenchPanelIds.LOG, active);
            entry.Command!.Execute(null);
            Pump(main);
            Assert.Equal(0, fixture.ViewModel.Log.UnreadErrorCount);
            Assert.Equal("日志", adapter.Title);
            session.Layouts.FloatPanel(WorkbenchPanelIds.LOG);
            Pump(main);
            var floating = Assert.Single(session.Layouts.FloatingWindows);
            floating.WindowState = WindowState.Minimized;
            fixture.ViewModel.Log.Error("离线测试", "浮窗未读错误");
            Pump(main);
            Assert.Contains("日志 (1)", floating.Title);
            floating.Close();
            Pump(main);
            session.Layouts.ShowPanel(WorkbenchPanelIds.LOG);
            Pump(main);
            Assert.Same(originalView, adapter.View);
            Assert.Contains(fixture.ViewModel.Log.Entries, item => item.Message == "待查看的错误");
            fixture.ViewModel.SelectedWorkspace = null;
            Pump(main);
            Assert.Same(originalView, adapter.View);
            Assert.Contains(fixture.ViewModel.Log.Entries, item => item.Message == "待查看的错误");
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    private static WorkspaceLayoutSnapshot CreateLegacyLayout()
    {
        return new()
        {
            Main = new()
            {
                PanelIds = [.. WorkbenchPanelIds.ForVersion(1).Where(id => id != WorkbenchPanelIds.LYRICS)],
                ActivePanelId = WorkbenchPanelIds.COMPOSER
            },
            FloatingWindows =
            [
                new()
                {
                    Layout = new() { PanelIds = [WorkbenchPanelIds.LYRICS], ActivePanelId = WorkbenchPanelIds.LYRICS },
                    Bounds = new() { X = -1200, Y = 50, Width = 600, Height = 400, Scaling = 2 }
                }
            ],
            FocusedPanelId = WorkbenchPanelIds.COMPOSER
        };
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
