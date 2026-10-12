using System.ComponentModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证整个工作区侧栏的折叠、恢复和布局保存。
/// </summary>
public class WorkspaceSidebarLayoutTests
{
    /// <summary>
    /// 单击标题栏按钮释放侧栏和分隔线宽度，再次单击恢复原宽度。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(200)]
    [InlineData(316)]
    [InlineData(360)]
    public async Task ToggleReclaimsWidthAndRestoresSidebar(double sidebarWidth)
    {
        using var context = new WorkspaceSidebarTestContext(sidebarWidth);
        var window = new MainWindow { DataContext = context.ViewModel };

        try
        {
            ShowWindow(window);
            var columns = window.FindControl<Grid>("MainColumns")!;
            var sidebar = window.GetVisualDescendants().OfType<WorkspaceSidebarView>().Single();
            var splitter = columns.Children.OfType<GridSplitter>().Single();
            var button = GetToggleButton(window);
            var selectedWorkspace = context.ViewModel.SelectedWorkspace;
            var selectedRoom = selectedWorkspace!.SelectedRoom;
            var expandedContentWidth = columns.ColumnDefinitions[2].ActualWidth;
            var splitterWidth = columns.ColumnDefinitions[1].ActualWidth;
            Assert.Equal(sidebarWidth, columns.ColumnDefinitions[0].ActualWidth, 1);
            CapturePreview(window, "expanded");

            await ClickToggleAsync(window, button);

            Assert.False(sidebar.IsVisible);
            Assert.False(splitter.IsVisible);
            Assert.True(button.IsEffectivelyVisible);
            Assert.Equal(0, columns.ColumnDefinitions[0].ActualWidth);
            Assert.Equal(0, columns.ColumnDefinitions[1].ActualWidth);
            Assert.Equal(expandedContentWidth + sidebarWidth + splitterWidth,
                columns.ColumnDefinitions[2].ActualWidth, 1);
            Assert.Same(selectedWorkspace, context.ViewModel.SelectedWorkspace);
            Assert.Same(selectedRoom, selectedWorkspace.SelectedRoom);
            AssertSavedLayout(context, true, sidebarWidth);
            CapturePreview(window, "collapsed");

            await ClickToggleAsync(window, button);

            Assert.True(sidebar.IsVisible);
            Assert.True(splitter.IsVisible);
            Assert.Equal(sidebarWidth, columns.ColumnDefinitions[0].ActualWidth, 1);
            Assert.Equal(splitterWidth, columns.ColumnDefinitions[1].ActualWidth, 1);
            Assert.Equal(expandedContentWidth, columns.ColumnDefinitions[2].ActualWidth, 1);
            AssertSavedLayout(context, false, sidebarWidth);
        }
        finally
        {
            CloseWindow(window);
        }
    }

    /// <summary>
    /// 从折叠配置启动时没有空白列，恢复后保留之前保存的展开宽度。
    /// </summary>
    [AvaloniaFact]
    public async Task StartsCollapsedAndRestoresSavedWidth()
    {
        using var context = new WorkspaceSidebarTestContext(316, true);
        var window = new MainWindow { DataContext = context.ViewModel };

        try
        {
            ShowWindow(window);
            var columns = window.FindControl<Grid>("MainColumns")!;
            var sidebar = window.GetVisualDescendants().OfType<WorkspaceSidebarView>().Single();
            var button = GetToggleButton(window);

            Assert.False(sidebar.IsVisible);
            Assert.Equal(0, columns.ColumnDefinitions[0].ActualWidth);
            Assert.Equal(0, columns.ColumnDefinitions[1].ActualWidth);
            Assert.Equal(columns.Bounds.Width, columns.ColumnDefinitions[2].ActualWidth, 1);
            Assert.True(button.IsEffectivelyVisible);

            await ClickToggleAsync(window, button);

            Assert.True(sidebar.IsVisible);
            Assert.Equal(316, columns.ColumnDefinitions[0].ActualWidth, 1);
            AssertSavedLayout(context, false, 316);
        }
        finally
        {
            CloseWindow(window);
        }
    }

    /// <summary>
    /// 关闭折叠窗口时不把隐藏列的零宽度写回展开宽度。
    /// </summary>
    [AvaloniaFact]
    public async Task ClosingCollapsedWindowPreservesExpandedWidth()
    {
        using var context = new WorkspaceSidebarTestContext(316);
        var window = new MainWindow { DataContext = context.ViewModel };

        try
        {
            ShowWindow(window);
            await ClickToggleAsync(window, GetToggleButton(window));
            context.ViewModel.StatusMessage = "等待关闭保存";
            var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var saveHandler = new PropertyChangedEventHandler((sender, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.StatusMessage) &&
                    sender is MainWindowViewModel viewModel &&
                    viewModel.StatusMessage.StartsWith("已保存", StringComparison.Ordinal))
                {
                    saved.TrySetResult();
                }
            });
            context.ViewModel.PropertyChanged += saveHandler;

            try
            {
                window.Close();
                await saved.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                context.ViewModel.PropertyChanged -= saveHandler;
            }

            AssertSavedLayout(context, true, 316);
        }
        finally
        {
            CloseWindow(window);
        }
    }

    private static Button GetToggleButton(Window window)
    {
        return window.GetVisualDescendants().OfType<Button>()
            .Single(control => control.Name == "SidebarToggleButton");
    }

    private static async Task ClickToggleAsync(Window window, Button button)
    {
        var command = Assert.IsType<IAsyncRelayCommand>(button.Command, exactMatch: false);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Assert.NotNull(command.ExecutionTask);
        await command.ExecutionTask;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void AssertSavedLayout(WorkspaceSidebarTestContext context, bool isCollapsed, double sidebarWidth)
    {
        using var settings = JsonDocument.Parse(File.ReadAllText(context.StateService.SettingsFilePath));
        Assert.Equal(isCollapsed, settings.RootElement.GetProperty("IsSidebarCollapsed").GetBoolean());
        Assert.Equal(sidebarWidth, context.StateService.Load().Settings.SidebarWidth);
    }

    private static void ShowWindow(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void CloseWindow(Window window)
    {
        window.DataContext = null;
        window.Close();
        Dispatcher.UIThread.RunJobs();
    }

    private static void CapturePreview(Window window, string state)
    {
        var directory = Environment.GetEnvironmentVariable("YOHUKE_SIDEBAR_QA_OUTPUT");

        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, $"sidebar-{state}.png"));
    }
}
