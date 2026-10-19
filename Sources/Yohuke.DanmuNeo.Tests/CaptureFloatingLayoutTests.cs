using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Dock.Model;
using Dock.Model.Core;
using Yohuke.DanmuNeo.Layouts;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证截图图库默认横向浮窗及重新打开、持久化和尺寸保留。</summary>
public class CaptureFloatingLayoutTests
{
    /// <summary>内置布局中首次打开为横向浮窗，重复打开保留尺寸，关闭后可重新打开。</summary>
    [AvaloniaTheory]
    [InlineData(WorkspaceLayoutPresets.DEFAULT)]
    [InlineData(WorkspaceLayoutPresets.TRANSLATION)]
    [InlineData(WorkspaceLayoutPresets.LYRICS)]
    [InlineData(WorkspaceLayoutPresets.FORWARDING)]
    public async Task GalleryOpensInOneHorizontalFloatingWindow(string preset)
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        context.Controller.ApplyPreset(preset);
        context.Pump();
        context.Controller.ShowPanel(WorkbenchPanelIds.LIVE_CAPTURES);
        context.Pump();
        var floating = Assert.Single(context.Controller.FloatingWindows);
        Assert.True(floating.IsVisible);
        Assert.True(floating.Width >= floating.Height * 1.4);
        Assert.Contains(floating, context.Views[WorkbenchPanelIds.LIVE_CAPTURES].GetVisualAncestors());
        floating.Width = 900;
        floating.Height = 560;
        context.Pump();
        context.Controller.ShowPanel(WorkbenchPanelIds.LIVE_CAPTURES);
        context.Controller.FloatPanel(WorkbenchPanelIds.LIVE_CAPTURES);
        context.Pump();
        Assert.Same(floating, Assert.Single(context.Controller.FloatingWindows));
        Assert.Equal(900, floating.Width, 1);
        Assert.Equal(560, floating.Height, 1);
        floating.Close();
        context.Pump();
        Assert.False(context.Controller.IsVisible(WorkbenchPanelIds.LIVE_CAPTURES));
        context.Controller.ShowPanel(WorkbenchPanelIds.LIVE_CAPTURES);
        context.Pump();
        var reopened = Assert.Single(context.Controller.FloatingWindows);
        Assert.True(reopened.Width > reopened.Height);
        Assert.NotSame(floating, reopened);
        Assert.True(WorkspaceLayoutValidator.TryValidate(context.Controller.Capture(), out var error), error);
    }

    /// <summary>浮窗尺寸在保存恢复后继续保留，其他工具仍按已有停靠位置打开。</summary>
    [AvaloniaFact]
    public async Task RestoredCaptureWindowKeepsItsSavedBoundsAndOtherPanelsStayDocked()
    {
        WorkspaceLayoutFile saved;
        await using (var context = new WorkbenchLayoutTestContext())
        {
            context.Show();
            context.Controller.ShowPanel(WorkbenchPanelIds.MODERATION);
            context.Controller.ShowPanel(WorkbenchPanelIds.ACCOUNT_MEDALS);
            context.Pump();
            Assert.Empty(context.Controller.FloatingWindows);
            context.Controller.ShowPanel(WorkbenchPanelIds.LIVE_CAPTURES);
            context.Pump();
            var floating = Assert.Single(context.Controller.FloatingWindows);
            floating.Width = 920;
            floating.Height = 570;
            context.Pump();
            await context.Controller.FlushAsync();
            saved = await context.Store.LoadAsync();
            var bounds = Assert.Single(saved.CurrentLayout.FloatingWindows).Bounds;
            Assert.Equal(920, bounds.Width, 1);
            Assert.Equal(570, bounds.Height, 1);
        }
        await using var restored = new WorkbenchLayoutTestContext(saved);
        restored.Show();
        var window = Assert.Single(restored.Controller.FloatingWindows);
        restored.Controller.ShowPanel(WorkbenchPanelIds.LIVE_CAPTURES);
        restored.Pump();
        Assert.Same(window, Assert.Single(restored.Controller.FloatingWindows));
        Assert.Equal(920, window.Width, 1);
        Assert.Equal(570, window.Height, 1);
    }

    /// <summary>用户主动停靠后，再次打开、隐藏恢复和跨启动恢复均保留停靠位置。</summary>
    [AvaloniaFact]
    public async Task UserDockedGalleryStaysDockedWhenReopenedAndRestored()
    {
        WorkspaceLayoutFile saved;
        await using (var context = new WorkbenchLayoutTestContext())
        {
            context.Show();
            context.Controller.ShowPanel(WorkbenchPanelIds.LIVE_CAPTURES);
            context.Pump();
            var panel = context.Controller.PanelAdapters[WorkbenchPanelIds.LIVE_CAPTURES];
            var target = Assert.IsAssignableFrom<IDock>(context.Controller.PanelAdapters[WorkbenchPanelIds.DANMU].Owner);
            var manager = new DockManager(new DockService());
            Assert.True(manager.ValidateDockable(panel, target, DragAction.Move, DockOperation.Fill, true));
            context.Pump();
            Assert.Empty(context.Controller.FloatingWindows);
            context.Controller.ShowPanel(WorkbenchPanelIds.LIVE_CAPTURES);
            context.Pump();
            Assert.Same(target, panel.Owner);
            context.Controller.HidePanel(WorkbenchPanelIds.LIVE_CAPTURES);
            context.Pump();
            context.Controller.ShowPanel(WorkbenchPanelIds.LIVE_CAPTURES);
            context.Pump();
            Assert.Same(target, panel.Owner);
            Assert.Empty(context.Controller.FloatingWindows);
            await context.Controller.FlushAsync();
            saved = await context.Store.LoadAsync();
        }
        await using var restored = new WorkbenchLayoutTestContext(saved);
        restored.Show();
        restored.Controller.ShowPanel(WorkbenchPanelIds.LIVE_CAPTURES);
        restored.Pump();
        Assert.Empty(restored.Controller.FloatingWindows);
        Assert.Same(restored.Owner, TopLevel.GetTopLevel(restored.Views[WorkbenchPanelIds.LIVE_CAPTURES]));
    }
}
