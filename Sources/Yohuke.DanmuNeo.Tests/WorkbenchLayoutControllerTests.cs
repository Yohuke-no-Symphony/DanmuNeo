using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.Layouts;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证 Dock 拓扑操作不会复制业务视图或丢失恢复入口。
/// </summary>
public class WorkbenchLayoutControllerTests
{
    /// <summary>
    /// 切换全部预设始终复用同一组面板，并将搬迁与用户关闭分开。
    /// </summary>
    [AvaloniaFact]
    public async Task ApplyingPresetsRetainsViewInstancesAndDoesNotCloseBusinessPanels()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var adapters = context.Controller.PanelAdapters.ToDictionary(pair => pair.Key, pair => pair.Value);
        var draft = Assert.IsType<TextBox>(context.Views[WorkbenchPanelIds.COMPOSER]);
        draft.Text = "保留的同传草稿";
        var closed = new List<string>();
        var starts = 0;
        var completions = 0;
        context.Controller.PanelClosedByUser += closed.Add;
        context.Controller.MigrationStarted += () => starts++;
        context.Controller.MigrationCompleted += () => completions++;

        foreach (var preset in WorkspaceLayoutPresets.BuiltIn)
        {
            context.Controller.ApplyPreset(preset.Id);
            context.Pump();
            Assert.Equal(preset.Id, context.Controller.CurrentPresetId);
            Assert.False(context.Controller.IsModified);
            Assert.True(WorkspaceLayoutValidator.TryValidate(context.Controller.Capture(), out var error), error);

            foreach (var id in WorkbenchPanelIds.All)
            {
                Assert.Same(adapters[id], context.Controller.PanelAdapters[id]);
                Assert.Same(context.Views[id], context.Controller.PanelAdapters[id].View);
            }
        }

        Assert.Equal("保留的同传草稿", draft.Text);
        Assert.Empty(closed);
        Assert.Equal(4, starts);
        Assert.Equal(starts, completions);
    }

    /// <summary>
    /// 最后一个面板也可以隐藏，随后菜单打开可恢复可用的主区域。
    /// </summary>
    [AvaloniaFact]
    public async Task AllHiddenPanelsCanBeRecoveredWithoutCreatingAnotherView()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();

        foreach (var id in WorkbenchPanelIds.All)
        {
            context.Controller.HidePanel(id);
        }

        context.Pump();
        var hidden = context.Controller.Capture();
        Assert.Null(hidden.Main);
        Assert.Equal(8, hidden.HiddenPanelIds.Count);
        Assert.True(WorkspaceLayoutValidator.TryValidate(hidden, out var hiddenError), hiddenError);
        context.Controller.ShowPanel(WorkbenchPanelIds.COMPOSER);
        context.Pump();

        Assert.True(context.Controller.IsVisible(WorkbenchPanelIds.COMPOSER));
        Assert.Equal(7, context.Controller.Capture().HiddenPanelIds.Count);
        Assert.Same(context.Views[WorkbenchPanelIds.COMPOSER], context.Controller.PanelAdapters[WorkbenchPanelIds.COMPOSER].View);
    }

    /// <summary>
    /// 用户关闭浮窗隐藏面板并通知业务释放播放器，主窗口继续存活。
    /// </summary>
    [AvaloniaFact]
    public async Task ClosingFloatingPlayerNotifiesUserCloseAndAllowsReopening()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var closed = new List<string>();
        context.Controller.PanelClosedByUser += closed.Add;
        context.Controller.FloatPanel(WorkbenchPanelIds.LIVE_PLAYER);
        context.Pump();
        var floating = Assert.Single(context.Controller.FloatingWindows);
        Assert.True(floating.IsVisible);
        Assert.Single(context.RegisteredWindows);

        floating.Close();
        context.Pump();

        Assert.True(context.Owner.IsVisible);
        Assert.Equal([WorkbenchPanelIds.LIVE_PLAYER], closed);
        Assert.False(context.Controller.IsVisible(WorkbenchPanelIds.LIVE_PLAYER));
        Assert.Empty(context.Controller.FloatingWindows);
        Assert.Single(context.UnregisteredWindows);
        context.Controller.ShowPanel(WorkbenchPanelIds.LIVE_PLAYER);
        context.Pump();
        Assert.True(context.Controller.IsVisible(WorkbenchPanelIds.LIVE_PLAYER));
        Assert.True(WorkspaceLayoutValidator.TryValidate(context.Controller.Capture(), out var error), error);
    }

    /// <summary>
    /// Dock 自带关闭命令和菜单隐藏都仅发出一次用户关闭通知。
    /// </summary>
    [AvaloniaFact]
    public async Task DockCloseAndMenuHideUseTheSameBusinessCloseBoundary()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var closed = new List<string>();
        context.Controller.PanelClosedByUser += closed.Add;
        context.Controller.RootDock.Factory!.CloseDockable(context.Controller.PanelAdapters[WorkbenchPanelIds.LIVE_PLAYER]);
        context.Pump();
        context.Controller.ShowPanel(WorkbenchPanelIds.LIVE_PLAYER);
        context.Controller.HidePanel(WorkbenchPanelIds.LIVE_PLAYER);
        context.Controller.HidePanel(WorkbenchPanelIds.LIVE_PLAYER);
        context.Pump();

        Assert.Equal(new[] { WorkbenchPanelIds.LIVE_PLAYER, WorkbenchPanelIds.LIVE_PLAYER }, closed);
    }

    /// <summary>
    /// 自动记忆当前布局不覆盖保存的预设，完整管理操作保持内置布局只读。
    /// </summary>
    [AvaloniaFact]
    public async Task PersonalPresetsRemainIndependentFromCurrentLayoutAutoSave()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        Assert.False(context.Controller.SaveCurrent());
        var id = context.Controller.SaveAs("  同传自用  ");
        Assert.Equal("同传自用", context.Controller.Presets.Single(preset => preset.Id == id).Name);
        context.Controller.HidePanel(WorkbenchPanelIds.HISTORY);
        context.Pump();
        await context.Controller.FlushAsync();
        var persisted = await context.Store.LoadAsync();
        Assert.Contains(WorkbenchPanelIds.HISTORY, persisted.CurrentLayout.HiddenPanelIds);
        Assert.DoesNotContain(WorkbenchPanelIds.HISTORY, Assert.Single(persisted.UserPresets).Layout.HiddenPanelIds);
        Assert.True(context.Controller.IsModified);

        Assert.True(context.Controller.SaveCurrent());
        Assert.False(context.Controller.IsModified);
        Assert.True(context.Controller.Rename(id, "新的名字"));
        Assert.False(context.Controller.Rename(WorkspaceLayoutPresets.DEFAULT, "不可改名"));
        Assert.Throws<ArgumentException>(() => context.Controller.SaveAs("默认"));
        context.Controller.ApplyPreset(WorkspaceLayoutPresets.DEFAULT);
        context.Controller.ApplyPreset(id);
        context.Pump();
        Assert.False(context.Controller.IsVisible(WorkbenchPanelIds.HISTORY));
        Assert.True(context.Controller.Delete(id));
        Assert.Equal(WorkspaceLayoutPresets.DEFAULT, context.Controller.CurrentPresetId);
        Assert.Equal(4, context.Controller.Presets.Count);
        Assert.False(context.Controller.IsVisible(WorkbenchPanelIds.HISTORY));
        Assert.True(context.Controller.IsModified);
    }

    /// <summary>
    /// 已恢复但尚未显示的浮窗不能用未初始化尺寸覆盖布局记录。
    /// </summary>
    [AvaloniaFact]
    public async Task RestoredFloatingBoundsSurviveBeforeShowingTheOwner()
    {
        var snapshot = new WorkspaceLayoutSnapshot
        {
            Main = new()
            {
                PanelIds = [.. WorkbenchPanelIds.All.Where(id => id != WorkbenchPanelIds.LIVE_PLAYER)],
                ActivePanelId = WorkbenchPanelIds.COMPOSER
            },
            FloatingWindows =
            [
                new()
                {
                    Layout = new() { PanelIds = [WorkbenchPanelIds.LIVE_PLAYER] },
                    Bounds = new() { X = 90, Y = 100, Width = 620, Height = 420, Scaling = 1 }
                }
            ],
            FocusedPanelId = WorkbenchPanelIds.COMPOSER
        };
        await using var context = new WorkbenchLayoutTestContext(new() { CurrentLayout = snapshot });
        var floating = Assert.Single(context.Controller.Capture().FloatingWindows);

        Assert.Equal(620, floating.Bounds.Width);
        Assert.Equal(420, floating.Bounds.Height);
        Assert.True(WorkspaceLayoutValidator.TryValidate(context.Controller.Capture(), out var error), error);
    }

    /// <summary>
    /// 程序隐藏保留播放器会话语义，退出先保存浮窗拓扑再清理宿主。
    /// </summary>
    [AvaloniaFact]
    public async Task ProgrammaticHideAndDisposalDoNotReportUserCloseOrPersistTeardown()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var closed = new List<string>();
        context.Controller.PanelClosedByUser += closed.Add;
        context.Controller.HidePanel(WorkbenchPanelIds.LIVE_PLAYER, false);
        context.Controller.ShowPanel(WorkbenchPanelIds.LIVE_PLAYER);
        context.Controller.FloatPanel(WorkbenchPanelIds.LIVE_PLAYER);
        context.Pump();
        Assert.Single(context.Controller.Capture().FloatingWindows);

        await context.Controller.DisposeAsync();
        context.Pump();
        var restored = await context.Store.LoadAsync();

        Assert.Empty(closed);
        Assert.Single(restored.CurrentLayout.FloatingWindows);
        Assert.DoesNotContain(WorkbenchPanelIds.LIVE_PLAYER, restored.CurrentLayout.HiddenPanelIds);
        Assert.Empty(context.Controller.FloatingWindows);
        Assert.Single(context.UnregisteredWindows);
    }

    /// <summary>
    /// 原生窗口关闭被取消时，浮窗内容和业务播放状态均保留。
    /// </summary>
    [AvaloniaFact]
    public async Task CancellingFloatingWindowCloseKeepsItsPanelsVisible()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var closed = new List<string>();
        context.Controller.PanelClosedByUser += closed.Add;
        context.Controller.FloatPanel(WorkbenchPanelIds.LIVE_PLAYER);
        context.Pump();
        var floating = Assert.Single(context.Controller.FloatingWindows);
        EventHandler<WindowClosingEventArgs> cancel = (_, args) => args.Cancel = true;
        floating.Closing += cancel;

        floating.Close();
        context.Pump();

        Assert.True(floating.IsVisible);
        Assert.True(context.Controller.IsVisible(WorkbenchPanelIds.LIVE_PLAYER));
        Assert.Single(context.Controller.Capture().FloatingWindows);
        Assert.Empty(closed);
        floating.Closing -= cancel;
    }

    /// <summary>
    /// 从菜单再次打开已浮动面板会恢复已有窗口，不会复制业务面板或创建额外浮窗。
    /// </summary>
    [AvaloniaFact]
    public async Task ShowingFloatingPanelReusesAndRestoresItsWindow()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        context.Controller.FloatPanel(WorkbenchPanelIds.LIVE_PLAYER);
        context.Pump();
        var floating = Assert.Single(context.Controller.FloatingWindows);
        floating.WindowState = WindowState.Minimized;

        context.Controller.ShowPanel(WorkbenchPanelIds.LIVE_PLAYER);
        context.Pump();

        Assert.Same(floating, Assert.Single(context.Controller.FloatingWindows));
        Assert.Equal(WindowState.Normal, floating.WindowState);
        Assert.Single(context.RegisteredWindows);
    }

    /// <summary>
    /// 同标签组交换顺序也必须在原生视图离开宿主前开启迁移作用域。
    /// </summary>
    [AvaloniaFact]
    public async Task SwappingTabsStartsMigrationWithoutReportingUserClose()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var starts = 0;
        var closed = new List<string>();
        context.Controller.MigrationStarted += () => starts++;
        context.Controller.PanelClosedByUser += closed.Add;
        var danmu = context.Controller.PanelAdapters[WorkbenchPanelIds.DANMU];
        var history = context.Controller.PanelAdapters[WorkbenchPanelIds.HISTORY];

        context.Controller.RootDock.Factory!.SwapDockable((Dock.Model.Core.IDock)danmu.Owner!, danmu, history);
        context.Pump();

        Assert.Equal(1, starts);
        Assert.Empty(closed);
        Assert.True(WorkspaceLayoutValidator.TryValidate(context.Controller.Capture(), out var error), error);
    }
}
