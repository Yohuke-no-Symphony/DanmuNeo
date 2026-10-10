using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 添加弹窗的真实绑定和布局测试。
/// </summary>
public class LiveRoomSearchViewTests
{
    /// <summary>
    /// 点击搜索结果的添加按钮会执行主视图模型命令。
    /// </summary>
    [AvaloniaFact]
    public async Task ResultAddButtonUsesMainViewModelCommand()
    {
        using var context = new LiveRoomSearchTestContext();
        var result = LiveRoomSearchTestContext.CreateResult();
        context.SearchService.RespondAsync = (_, _) => Task.FromResult(new LiveRoomSearchPage { Results = [result] });
        var viewModel = context.ViewModel;
        var target = viewModel.Workspaces[0];
        viewModel.OpenAddRoomDialog(target);
        viewModel.LiveRoomSearchText = "主播";
        await viewModel.SearchLiveRoomsAsync();
        var view = new AddWorkspaceDialogView { DataContext = viewModel };
        var window = new Window { Width = 820, Height = 600, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var button = view.GetVisualDescendants().OfType<Button>()
                .Single(control => control.Name == "AddSearchedRoomButton");
            Assert.Same(viewModel.AddSearchedRoomCommand, button.Command);
            Assert.Same(result, button.CommandParameter);
            Assert.True(button.IsEffectivelyEnabled);
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!
                .Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            var execution = viewModel.AddSearchedRoomCommand.ExecutionTask;
            Assert.NotNull(execution);
            await execution;

            Assert.Equal("100", Assert.Single(target.Rooms).RoomId);
            Assert.False(viewModel.IsAddDialogOpen);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 窄高窗口中结果可滚动，操作区始终留在可见范围。
    /// </summary>
    [AvaloniaFact]
    public void ShortDialogKeepsFooterVisible()
    {
        using var context = new LiveRoomSearchTestContext();
        var viewModel = context.ViewModel;
        viewModel.OpenAddRoomDialog(null);

        for (var index = 0; index < 42; index++)
        {
            viewModel.LiveRoomSearchResults.Add(new()
            {
                RoomId = (100 + index).ToString(),
                OwnerUid = "42",
                OwnerName = new string('长', 80),
                Title = new string('题', 80)
            });
        }

        var view = new AddWorkspaceDialogView { DataContext = viewModel };
        var window = new Window { Width = 820, Height = 450, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var expander = view.GetVisualDescendants().OfType<Expander>().Single();
            expander.IsExpanded = true;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            foreach (var button in view.GetVisualDescendants().OfType<Button>()
                         .Where(control => control.Content is "取消" or "手动添加"))
            {
                var point = button.TranslatePoint(new Point(0, button.Bounds.Height), window)!.Value;
                Assert.InRange(point.Y, 0, window.Bounds.Height);
            }

            var results = view.GetVisualDescendants().OfType<ItemsControl>()
                .Single(control => control.Name == "LiveRoomSearchResultsControl");
            var scrollViewer = results.GetVisualAncestors().OfType<ScrollViewer>().First();
            Assert.True(scrollViewer.Extent.Height > scrollViewer.Viewport.Height);
            Assert.True(scrollViewer.Extent.Width <= scrollViewer.Viewport.Width + 0.5);
            var previewDirectory = Environment.GetEnvironmentVariable("YOHUKE_SEARCH_QA_OUTPUT");

            if (!string.IsNullOrWhiteSpace(previewDirectory))
            {
                Directory.CreateDirectory(previewDirectory);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(previewDirectory, "live-room-search.png"));
            }
        }
        finally
        {
            window.Close();
        }
    }
}
