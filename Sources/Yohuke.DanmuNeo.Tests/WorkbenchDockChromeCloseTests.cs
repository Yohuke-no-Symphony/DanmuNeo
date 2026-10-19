using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Dock.Model.Core;
using Dock.Model.Core.Events;
using Yohuke.DanmuNeo.Layouts;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证实际点击面板关闭按钮时只执行一次关闭，并保留业务视图与恢复入口。
/// </summary>
public class WorkbenchDockChromeCloseTests
{
    /// <summary>
    /// 浮窗最后一个面板首次浮动或重新停靠再浮动后都能关闭并恢复草稿。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClickingLastFloatingPanelClosePreservesDraftAndAllowsReopening(bool floatAgain)
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var composer = context.Controller.PanelAdapters[WorkbenchPanelIds.COMPOSER];
        var input = Assert.IsType<TextBox>(composer.View);
        input.Text = "保留的同传草稿";
        var closed = new List<string>();
        context.Controller.PanelClosedByUser += closed.Add;
        context.Controller.FloatPanel(composer.Id);
        context.Pump();
        var floating = Assert.Single(context.Controller.FloatingWindows);
        floating.UpdateLayout();

        if (floatAgain)
        {
            var mainDock = Assert.IsAssignableFrom<IDock>(context.Controller.PanelAdapters[WorkbenchPanelIds.DANMU].Owner);
            context.Controller.RootDock.Factory!.MoveDockable((IDock)composer.Owner!, mainDock, composer, null);
            context.Pump();
            Assert.False(floating.IsVisible);
            Assert.Empty(context.Controller.FloatingWindows);
            context.Controller.FloatPanel(composer.Id);
            context.Pump();
            floating = Assert.Single(context.Controller.FloatingWindows);
            floating.UpdateLayout();
        }

        var unregisteredCount = context.UnregisteredWindows.Count;
        ClickClose(floating, composer.Owner);
        context.Pump();

        Assert.True(context.Owner.IsVisible);
        Assert.False(floating.IsVisible);
        Assert.Equal([composer.Id], closed);
        Assert.Same(floating, Assert.Single(context.UnregisteredWindows.Skip(unregisteredCount)));
        Assert.Empty(context.Controller.FloatingWindows);
        Assert.False(context.Controller.IsVisible(composer.Id));
        await context.Controller.FlushAsync();
        var saved = await context.Store.LoadAsync();
        Assert.Contains(composer.Id, saved.CurrentLayout.HiddenPanelIds);
        Assert.Empty(saved.CurrentLayout.FloatingWindows);
        Assert.True(WorkspaceLayoutValidator.TryValidate(saved, out var error), error);

        context.Controller.ShowPanel(composer.Id);
        context.Pump();

        Assert.Same(composer, context.Controller.PanelAdapters[composer.Id]);
        Assert.Same(input, context.Controller.PanelAdapters[composer.Id].View);
        Assert.Same(context.Owner, TopLevel.GetTopLevel(input));
        Assert.Equal("保留的同传草稿", input.Text);
        Assert.True(input.Focus());
        input.CaretIndex = input.Text.Length;
        context.Owner.KeyTextInput("继续输入");
        Assert.Equal("保留的同传草稿继续输入", input.Text);

        ClickClose(context.Owner, composer.Owner);
        context.Pump();

        Assert.Equal(new[] { composer.Id, composer.Id }, closed);
        Assert.False(context.Controller.IsVisible(composer.Id));
        Assert.True(context.Owner.IsVisible);
    }

    /// <summary>
    /// 浮窗内逐个关闭面板时保留其余标签，最后一个面板关闭后只注销一次浮窗。
    /// </summary>
    [AvaloniaFact]
    public async Task ClickingSharedFloatingPanelCloseKeepsOtherPanelUntilLastClose()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var composer = context.Controller.PanelAdapters[WorkbenchPanelIds.COMPOSER];
        var history = context.Controller.PanelAdapters[WorkbenchPanelIds.HISTORY];
        var factory = context.Controller.RootDock.Factory!;
        var closed = new List<string>();
        context.Controller.PanelClosedByUser += closed.Add;
        context.Controller.FloatPanel(composer.Id);
        factory.MoveDockable((IDock)history.Owner!, (IDock)composer.Owner!, history, composer);
        context.Controller.ShowPanel(composer.Id);
        context.Pump();
        var floating = Assert.Single(context.Controller.FloatingWindows);
        floating.UpdateLayout();

        ClickClose(floating, composer.Owner);
        context.Pump();

        Assert.True(floating.IsVisible);
        Assert.Same(floating, Assert.Single(context.Controller.FloatingWindows));
        Assert.True(context.Controller.IsVisible(history.Id));
        Assert.False(context.Controller.IsVisible(composer.Id));
        Assert.Empty(context.UnregisteredWindows);
        Assert.Equal([composer.Id], closed);

        floating.UpdateLayout();
        ClickClose(floating, history.Owner);
        context.Pump();

        Assert.False(floating.IsVisible);
        Assert.Empty(context.Controller.FloatingWindows);
        Assert.Equal([floating], context.UnregisteredWindows);
        Assert.Equal(new[] { composer.Id, history.Id }, closed);
        Assert.True(context.Owner.IsVisible);
        Assert.True(WorkspaceLayoutValidator.TryValidate(context.Controller.Capture(), out var error), error);
    }

    /// <summary>
    /// 面板关闭被取消时不再触发宿主关闭，解除取消后仍可使用同一个按钮关闭。
    /// </summary>
    [AvaloniaFact]
    public async Task CancellingPanelChromeCloseKeepsFloatingPanelAndAllowsRetry()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var composer = context.Controller.PanelAdapters[WorkbenchPanelIds.COMPOSER];
        var factory = context.Controller.RootDock.Factory!;
        var closed = new List<string>();
        context.Controller.PanelClosedByUser += closed.Add;
        context.Controller.FloatPanel(composer.Id);
        context.Pump();
        var floating = Assert.Single(context.Controller.FloatingWindows);
        floating.UpdateLayout();
        EventHandler<DockableClosingEventArgs> cancel = (_, args) => args.Cancel = ReferenceEquals(args.Dockable, composer);
        factory.DockableClosing += cancel;

        try
        {
            ClickClose(floating, composer.Owner);
            context.Pump();

            Assert.True(floating.IsVisible);
            Assert.True(context.Controller.IsVisible(composer.Id));
            Assert.Same(floating, Assert.Single(context.Controller.FloatingWindows));
            Assert.Empty(context.UnregisteredWindows);
            Assert.Empty(closed);
        }
        finally
        {
            factory.DockableClosing -= cancel;
        }

        ClickClose(floating, composer.Owner);
        context.Pump();

        Assert.False(floating.IsVisible);
        Assert.False(context.Controller.IsVisible(composer.Id));
        Assert.Equal([composer.Id], closed);
        Assert.Equal([floating], context.UnregisteredWindows);
    }

    private static WorkbenchToolChromeControl GetChrome(Window window, IDockable? owner)
    {
        return window.GetVisualDescendants().OfType<WorkbenchToolChromeControl>()
            .Single(control => ReferenceEquals(control.DataContext, owner));
    }

    private static void ClickClose(Window window, IDockable? owner)
    {
        var chrome = GetChrome(window, owner);
        var close = chrome.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Name == "PART_CloseButton" && ReferenceEquals(button.TemplatedParent, chrome));
        var dock = Assert.IsAssignableFrom<IDock>(owner);
        Assert.NotNull(dock.ActiveDockable);
        Assert.Same(dock.ActiveDockable, close.CommandParameter);
        Assert.True(close.IsEffectivelyVisible);
        Assert.True(close.IsEffectivelyEnabled);
        Assert.True(close.Bounds.Width > 0 && close.Bounds.Height > 0);
        var point = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }
}
