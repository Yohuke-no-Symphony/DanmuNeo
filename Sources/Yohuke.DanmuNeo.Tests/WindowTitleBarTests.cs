using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Models.BrowserLogin;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证共享标题栏在辅助窗口、独立窗口和 Dock 浮窗中的组合与输入边界。
/// </summary>
public class WindowTitleBarTests
{
    /// <summary>
    /// 标题绑定不依赖业务数据上下文，包装保留原内容，关闭后释放标题订阅。
    /// </summary>
    [AvaloniaFact]
    public void FramePreservesContentAndTracksTitleWithoutWorkbenchDataContext()
    {
        var context = new object();
        var content = new Border { Child = new TextBox { Text = "保留草稿" } };
        var window = new Window
        {
            Width = 640,
            Height = 360,
            Title = "辅助窗口",
            DataContext = context,
            Content = content
        };
        var titleBar = WindowChrome.AttachFrame(window);

        try
        {
            window.Show();
            PumpLayout(window);
            Assert.Same(titleBar, Assert.Single(window.GetVisualDescendants().OfType<WindowTitleBar>()));
            Assert.Same(context, content.DataContext);
            Assert.Equal("保留草稿", Assert.IsType<TextBox>(content.Child).Text);
            Assert.Equal(window.Title, titleBar.Title);
            Assert.Null(titleBar.HeaderContent);
            Assert.Null(titleBar.RightContent);
            Assert.Empty(titleBar.GetVisualDescendants().OfType<Menu>());
            var titleBottom = GetBounds(titleBar, window).Bottom;
            var contentBounds = GetBounds(content, window);
            Assert.InRange(Math.Abs(contentBounds.Top - titleBottom), 0, 0.5);
            Assert.InRange(Math.Abs(contentBounds.Bottom - window.ClientSize.Height), 0, 0.5);

            window.Title = new string('长', 120);
            PumpLayout(window);
            Assert.Equal(window.Title, titleBar.Title);
            var titleText = titleBar.FindControl<TextBlock>("TitleText")!;
            Assert.InRange(GetBounds(titleText, window).Right, 1, window.ClientSize.Width);
            window.Close();
            var closedTitle = titleBar.Title;
            window.Title = "关闭后标题";
            PumpLayout(window);
            Assert.Equal(closedTitle, titleBar.Title);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 原生按钮保留区与窗口操作不触发拖拽，操作按钮仍接收实际鼠标点击。
    /// </summary>
    [AvaloniaFact]
    public void CaptionInsetsAndActionButtonsRemainOutsideDragRegion()
    {
        var clicks = 0;
        var action = new Button { Content = "操作" };
        action.Click += (_, _) => clicks++;
        var window = new Window { Width = 640, Height = 360, Title = "窗口操作", Content = new Border() };
        var titleBar = WindowChrome.AttachFrame(window, action);

        try
        {
            window.Show();
            titleBar.CaptionInsets = new(92, 0, 146, 0);
            PumpLayout(window);
            var titleBounds = GetBounds(titleBar, window);
            var actionBounds = GetBounds(action, window);
            Assert.False(titleBar.IsDragRegion(new(titleBounds.Left + 46, titleBounds.Center.Y)));
            Assert.False(titleBar.IsDragRegion(new(titleBounds.Right - 73, titleBounds.Center.Y)));
            Assert.False(titleBar.IsDragRegion(actionBounds.Center));
            Assert.True(titleBar.IsDragRegion(GetBounds(titleBar.FindControl<TextBlock>("TitleText")!, window).Center));
            Assert.True(actionBounds.Right <= titleBounds.Right - titleBar.CaptionInsets.Right);
            Click(window, actionBounds.Center);
            Assert.Equal(1, clicks);
            Assert.Equal(WindowState.Normal, window.WindowState);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 标题内容中的按钮在禁用或不接受焦点时仍保留交互区域，普通文字允许拖拽。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void HeaderButtonsKeepTheirRegionRegardlessOfEnabledAndFocusable(bool enabled, bool focusable)
    {
        var label = new TextBlock { Text = "可拖动标题", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        var action = new Button
        {
            Content = "操作",
            IsEnabled = enabled,
            Focusable = focusable,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetColumn(action, 1);
        var header = new Grid { ColumnDefinitions = new("Auto,*"), Children = { label, action } };
        var window = new Window { Width = 640, Height = 360, Content = new Border() };
        var titleBar = WindowChrome.AttachFrame(window);
        titleBar.HeaderContent = header;

        try
        {
            window.Show();
            PumpLayout(window);
            Assert.True(titleBar.IsDragRegion(GetBounds(label, window).Center));
            Assert.False(titleBar.IsDragRegion(GetBounds(action, window).Center));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 两种辅助窗口尺寸策略都将原业务表面完整放在标题栏下方。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AuxiliarySizingKeepsBodyBelowSharedTitleBar(bool contentHeight)
    {
        var owner = new Window { Width = 1000, Height = 740 };
        using var windows = new WorkbenchWindowRegistry(owner);
        var body = new Border { MinHeight = 140, Child = new TextBlock { Text = "辅助窗口内容" } };

        try
        {
            owner.Show();
            var window = windows.OpenAuxiliary("title-test", "辅助标题", () => body,
                width: 480, height: 360,
                sizing: contentHeight ? WorkbenchAuxiliarySizing.ContentHeight : WorkbenchAuxiliarySizing.Resizable);
            PumpLayout(window);
            var titleBar = Assert.Single(window.GetVisualDescendants().OfType<WindowTitleBar>());
            var bodyBounds = GetBounds(body, window);
            Assert.Equal(window.Title, titleBar.Title);
            Assert.Null(titleBar.HeaderContent);
            Assert.InRange(Math.Abs(bodyBounds.Top - GetBounds(titleBar, window).Bottom), 0, 0.5);
            Assert.InRange(Math.Abs(bodyBounds.Bottom - window.ClientSize.Height), 0, 0.5);
            Assert.Equal(contentHeight, !window.CanResize);
        }
        finally
        {
            windows.Dispose();
            owner.Close();
        }
    }

    /// <summary>
    /// 歌词编辑、离线样式预览和浏览器登录窗口均使用唯一的共享标题栏。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void StandaloneWindowsUseOneSharedTitleBar(int kind)
    {
        var window = CreateStandaloneWindow(kind);

        try
        {
            window.Show();
            PumpLayout(window);
            var titleBar = Assert.Single(window.GetVisualDescendants().OfType<WindowTitleBar>());
            Assert.Equal(window.Title, titleBar.Title);
            Assert.Null(titleBar.HeaderContent);
            Assert.Null(titleBar.RightContent);
            Assert.Empty(titleBar.GetVisualDescendants().OfType<Menu>());
            Assert.Empty(window.GetVisualDescendants().OfType<TitleBarView>());
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Dock 浮窗保留原面板实例，并只在窗口标题栏提供一个独立置顶入口。
    /// </summary>
    [AvaloniaFact]
    public async Task FloatingTitleBarOwnsIndependentPinAndPreservesDockContent()
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var main = new MainWindow { DataContext = context.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            var composer = workbench.Layouts.PanelAdapters[WorkbenchPanelIds.COMPOSER].View;
            workbench.Layouts.FloatPanel(WorkbenchPanelIds.COMPOSER);
            PumpLayout(main);
            var floating = Assert.Single(workbench.Layouts.FloatingWindows);
            PumpLayout(floating);
            var titleBar = Assert.Single(floating.GetVisualDescendants().OfType<WindowTitleBar>());
            Assert.Equal(floating.Title, titleBar.Title);
            Assert.Null(titleBar.HeaderContent);
            Assert.Contains(composer, floating.GetVisualDescendants());
            var pin = Assert.Single(floating.GetVisualDescendants().OfType<FloatingWindowPinButton>());
            Assert.Same(pin, titleBar.RightContent);
            var button = pin.FindControl<Button>("WindowTopmostButton")!;
            Assert.False(titleBar.IsDragRegion(GetBounds(button, floating).Center));
            Click(floating, GetBounds(button, floating).Center);
            Assert.True(WorkbenchWindowTopmost.GetIsIndependentlyTopmost(floating));
            Assert.True(floating.Topmost);
            Assert.False(main.Topmost);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
        }
    }

    private static Window CreateStandaloneWindow(int kind)
    {
        return kind switch
        {
            0 => new LyricEditDialogWindow(),
            1 => new DanmuStylePreviewWindow(),
            2 => new BrowserCookieLoginWindow(BrowserLoginPlatform.Bilibili,
                () => throw new InvalidOperationException("离线标题栏测试不创建浏览器。")),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private static Rect GetBounds(Control control, Window window)
    {
        return new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);
    }

    private static void Click(Window window, Point point)
    {
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        PumpLayout(window);
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
