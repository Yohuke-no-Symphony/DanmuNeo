using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Controls.ProportionalStackPanel;
using Dock.Model.Core;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证业务视图的动态最小尺寸会约束停靠比例和浮窗内容区域。
/// </summary>
public class WorkbenchLayoutMinimumTests
{
    /// <summary>
    /// Dock 新建分组的未赋值比例、隐藏组和未初始化分隔模板在两个方向均可测量。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(Avalonia.Layout.Orientation.Horizontal)]
    [InlineData(Avalonia.Layout.Orientation.Vertical)]
    public void UnassignedProportionsAndTransientEmptyGroupsHaveFiniteBounds(Avalonia.Layout.Orientation orientation)
    {
        var stack = new WorkbenchProportionalStackPanel { Orientation = orientation };
        var first = new ContentPresenter { Content = new Border() };
        var second = new ContentPresenter { Content = new Border() };
        var splitter = new ContentPresenter { Content = new WorkbenchProportionalStackPanelSplitter() };
        stack.Children.Add(first);
        stack.Children.Add(splitter);
        stack.Children.Add(second);

        foreach (var collapsed in new[] { false, true, false })
        {
            ProportionalStackPanel.SetIsCollapsed(first, collapsed);
            ProportionalStackPanel.SetProportion(first, double.NaN);
            ProportionalStackPanel.SetCollapsedProportion(first, double.NaN);
            ProportionalStackPanel.SetProportion(second, double.NaN);
            ProportionalStackPanel.SetCollapsedProportion(second, double.NaN);
            stack.InvalidateMeasure();
            stack.Measure(new(400, 400));
            stack.Arrange(new(0, 0, 400, 400));

            Assert.All(stack.Children, child =>
            {
                Assert.True(double.IsFinite(child.Bounds.Width));
                Assert.True(double.IsFinite(child.Bounds.Height));
                Assert.True(double.IsFinite(child.Bounds.X));
                Assert.True(double.IsFinite(child.Bounds.Y));
            });
        }

        stack.Children.Remove(first);
        stack.Children.Remove(second);
        stack.Measure(new(400, 400));
        stack.Arrange(new(0, 0, 400, 400));
        Assert.True(double.IsFinite(splitter.Bounds.Width));
        Assert.True(double.IsFinite(splitter.Bounds.Height));
    }

    /// <summary>
    /// 比例尚未赋值的首次测量和后续插入分隔条使用有限尺寸，重复布局不重建已有模板内容。
    /// </summary>
    [AvaloniaFact]
    public void InitialMeasureAndInsertedSplitterKeepFiniteBoundsAndExistingContent()
    {
        var builds = 0;
        var first = new ContentPresenter
        {
            Content = new object(),
            ContentTemplate = new FuncDataTemplate<object>((_, _) =>
            {
                builds++;
                return new Border();
            })
        };
        var second = new Border { MinHeight = 124 };
        var splitter = new ContentPresenter { Content = new ProportionalStackPanelSplitter { Thickness = 9 } };
        var stack = new WorkbenchProportionalStackPanel();
        stack.Children.Add(first);
        stack.Children.Add(splitter);
        stack.Children.Add(second);
        stack.Measure(new(400, 400));
        stack.Arrange(new(0, 0, 400, 400));
        var content = first.Child;

        Assert.NotNull(content);
        Assert.True(double.IsNaN(ProportionalStackPanel.GetProportion(first)));
        Assert.Equal(9, splitter.Bounds.Height);
        stack.InvalidateMeasure();
        stack.Measure(new(400, 400));
        stack.Arrange(new(0, 0, 400, 400));
        Assert.Same(content, first.Child);
        Assert.Equal(1, builds);

        var inserted = new ContentPresenter { Content = new ProportionalStackPanelSplitter { Thickness = 13 } };
        stack.Children.Add(inserted);
        stack.Children.Add(new Border());
        stack.Measure(new(400, 400));
        stack.Arrange(new(0, 0, 400, 400));

        Assert.Equal(13, inserted.Bounds.Height);
        Assert.Same(content, first.Child);
        Assert.Equal(1, builds);
        Assert.All(stack.Children, child =>
        {
            Assert.True(double.IsFinite(child.Bounds.Height));
            Assert.InRange(child.Bounds.Bottom, 0, stack.Bounds.Height);
        });
    }

    /// <summary>
    /// 输入所需高度增大后，即使分隔比例很小也必须给实际内容留足空间。
    /// </summary>
    [AvaloniaFact]
    public async Task ContentMinimumConstrainsSmallDockProportions()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var view = context.Views[WorkbenchPanelIds.COMPOSER];
        var dock = Assert.IsAssignableFrom<IDock>(context.Controller.PanelAdapters[WorkbenchPanelIds.COMPOSER].Owner);
        view.MinHeight = 180;
        dock.Proportion = 0.02;
        Pump(context);

        Assert.True(dock.MinHeight > view.MinHeight);
        Assert.True(Assert.IsAssignableFrom<IDock>(dock.Owner).MinHeight >= dock.MinHeight);
        AssertFits(view, context.Owner);
        view.MinHeight = 240;
        Pump(context);
        Assert.True(dock.MinHeight > 240);
        AssertFits(view, context.Owner);
    }

    /// <summary>
    /// 未激活标签的最小尺寸同样保护整组，移出该标签后下限应随实际成员降低。
    /// </summary>
    [AvaloniaFact]
    public async Task InactivePanelMinimumUpdatesItsGroupAndReleasesAfterRemoval()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var composer = context.Controller.PanelAdapters[WorkbenchPanelIds.COMPOSER];
        var danmu = context.Controller.PanelAdapters[WorkbenchPanelIds.DANMU];
        var target = Assert.IsAssignableFrom<IDock>(danmu.Owner);
        context.Controller.RootDock.Factory!.MoveDockable(Assert.IsAssignableFrom<IDock>(composer.Owner), target, composer, danmu);
        context.Controller.ShowPanel(WorkbenchPanelIds.DANMU);
        Pump(context);
        composer.View.MinHeight = 220;
        Pump(context);
        var protectedHeight = target.MinHeight;

        Assert.True(protectedHeight > 220);
        context.Controller.ShowPanel(WorkbenchPanelIds.COMPOSER);
        Pump(context);
        AssertFits(composer.View, context.Owner);
        context.Controller.HidePanel(WorkbenchPanelIds.COMPOSER, false);
        Pump(context);
        Assert.True(target.MinHeight < protectedHeight);
    }

    /// <summary>
    /// 浮窗中的动态内容下限增大时，窗口最小尺寸必须包含 Dock 的标题和边框。
    /// </summary>
    [AvaloniaFact]
    public async Task FloatingHostMinimumIncludesItsContentAndChrome()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        context.Controller.FloatPanel(WorkbenchPanelIds.COMPOSER);
        Pump(context);
        var window = Assert.Single(context.Controller.FloatingWindows);
        var view = context.Views[WorkbenchPanelIds.COMPOSER];
        view.MinHeight = 300;
        Pump(context);

        Assert.True(window.MinHeight > view.MinHeight);
        AssertFits(view, window);
    }

    /// <summary>
    /// 临时错误高度和窗口缩放只改变实际分配，保存偏好保持不变且空间恢复后回到原比例。
    /// </summary>
    [AvaloniaFact]
    public async Task TemporaryMinimumAndWindowResizeDoNotRewritePreferredProportions()
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var view = context.Views[WorkbenchPanelIds.COMPOSER];
        view.MinHeight = 124;
        Pump(context);
        var preferred = WorkspaceLayoutSerializer.Serialize(context.Controller.CaptureFile());
        var originalHeight = view.Bounds.Height;

        context.Owner.Height = 600;
        view.MinHeight = 300;
        Pump(context);

        AssertFits(view, context.Owner);
        Assert.Equal(preferred, WorkspaceLayoutSerializer.Serialize(context.Controller.CaptureFile()));
        await context.Controller.FlushAsync();
        Assert.Equal(preferred, WorkspaceLayoutSerializer.Serialize(await context.Store.LoadAsync()));

        view.MinHeight = 124;
        context.Owner.Height = 800;
        Pump(context);

        Assert.Equal(preferred, WorkspaceLayoutSerializer.Serialize(context.Controller.CaptureFile()));
        Assert.InRange(view.Bounds.Height, originalHeight - 0.5, originalHeight + 0.5);
    }

    /// <summary>
    /// 下限生效时拖动从可见边界开始，单击和未提交的预览不会覆盖用户原比例。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SplitterDragStartsAtVisibleBoundaryWithoutChangingPreferencesOnClick(bool preview)
    {
        await using var context = new WorkbenchLayoutTestContext();
        context.Show();
        var view = context.Views[WorkbenchPanelIds.COMPOSER];
        view.MinHeight = 300;
        Pump(context);
        var group = Assert.IsAssignableFrom<IDock>(context.Controller.PanelAdapters[WorkbenchPanelIds.COMPOSER].Owner);
        var stack = context.Owner.GetVisualDescendants().OfType<WorkbenchProportionalStackPanel>()
            .Single(candidate => ReferenceEquals(candidate.DataContext, group.Owner));
        var splitter = Assert.Single(stack.Children.Select(WorkbenchProportionalStackPanel.GetSplitter)
            .OfType<ProportionalStackPanelSplitter>());
        splitter.PreviewResize = preview;
        var point = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), context.Owner)!.Value;
        var preferred = WorkspaceLayoutSerializer.Serialize(context.Controller.CaptureFile());
        var previousHeight = view.Bounds.Height;

        context.Owner.MouseDown(point, MouseButton.Left);
        context.Owner.MouseUp(point, MouseButton.Left);
        Pump(context);
        Assert.Equal(preferred, WorkspaceLayoutSerializer.Serialize(context.Controller.CaptureFile()));

        var destination = point - new Vector(0, 30);
        context.Owner.MouseDown(point, MouseButton.Left);
        context.Owner.MouseMove(destination);

        if (preview)
        {
            Pump(context);
            Assert.Equal(preferred, WorkspaceLayoutSerializer.Serialize(context.Controller.CaptureFile()));
        }

        context.Owner.MouseUp(destination, MouseButton.Left);
        Pump(context);

        Assert.InRange(view.Bounds.Height - previousHeight, 28, 32);
        Assert.NotEqual(preferred, WorkspaceLayoutSerializer.Serialize(context.Controller.CaptureFile()));
        AssertFits(view, context.Owner);
    }

    /// <summary>
    /// 全部实际面板竖排并显示发送错误时，最小主窗内仍容纳内容声明的高度下限。
    /// </summary>
    [AvaloniaFact]
    public async Task EightRealPanelsFitMinimumWindowWithComposerError()
    {
        using var fixture = new WorkspaceSidebarTestContext(240);
        fixture.ViewModel.Settings.WindowWidth = 980;
        fixture.ViewModel.Settings.WindowHeight = 640;
        fixture.ViewModel.Settings.UseWindowMenuOnMac = true;
        var file = WorkspaceLayoutPresets.CreateDefaultFile() with
        {
            CurrentLayout = new()
            {
                Main = new()
                {
                    Kind = WorkspaceLayoutNodeKind.Split,
                    Orientation = WorkspaceLayoutOrientation.Vertical,
                    Children = WorkbenchPanelIds.All.Select(id => new WorkspaceLayoutNode
                    {
                        Proportion = 1d / WorkbenchPanelIds.All.Count,
                        PanelIds = [id],
                        ActivePanelId = id
                    }).ToList()
                },
                FocusedPanelId = WorkbenchPanelIds.COMPOSER
            }
        };
        await new WorkspaceLayoutStore(fixture.ViewModel.ConfigDirectory).SaveAsync(file);
        var window = new MainWindow { DataContext = fixture.ViewModel };

        try
        {
            window.Show();
            await window.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(window.Workbench);
            workbench.Context.CurrentRoom!.DraftSendError = "发送失败：" + new string('错', 100);
            workbench.Context.CurrentRoom.IsDraftSendFailed = true;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.InRange(workbench.Layouts.RootDock.MinHeight, 0, workbench.Layouts.Host.Bounds.Height);

            foreach (var panel in workbench.Layouts.PanelAdapters.Values)
            {
                Assert.True(panel.View.IsEffectivelyVisible, panel.Id);
                Assert.True(panel.View.Bounds.Height > 0, panel.Id);
                AssertFits(panel.View, window);
            }

            Assert.True(workbench.Panels.Composer.Input.Bounds.Height >= 64);
        }
        finally
        {
            await window.CloseWorkbenchAsync();
        }
    }

    private static void AssertFits(Control view, Window window)
    {
        var tool = Assert.Single(view.GetVisualAncestors().OfType<ToolControl>());
        var bottom = view.TranslatePoint(new Point(0, view.Bounds.Height), tool)!.Value.Y;
        Assert.True(bottom <= tool.Bounds.Height + 0.5, $"内容底部 {bottom} 超过工具组 {tool.Bounds.Height}");
        var windowBottom = view.TranslatePoint(new Point(0, view.Bounds.Height), window)!.Value.Y;
        Assert.True(windowBottom <= window.Bounds.Height + 0.5,
            $"内容底部 {windowBottom} 超过窗口 {window.Bounds.Height}；" +
            string.Join(" / ", view.GetVisualAncestors().OfType<Control>().Select(control =>
                $"{control.GetType().Name}={control.Bounds}, min={control.MinHeight}")));
    }

    private static void Pump(WorkbenchLayoutTestContext context)
    {
        context.Pump();

        foreach (var window in context.Controller.FloatingWindows)
        {
            window.UpdateLayout();
        }

        Dispatcher.UIThread.RunJobs();
        context.Owner.UpdateLayout();
    }
}
