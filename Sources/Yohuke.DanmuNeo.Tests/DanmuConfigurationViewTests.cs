using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 使用正式控件验证弹幕配置选择器的布局、绑定和生命周期。
/// </summary>
public class DanmuConfigurationViewTests
{
    /// <summary>
    /// 配置控件在输入框底部按宽度换行且不重叠，绑定改变立即保存。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, 760)]
    [InlineData(true, 760)]
    [InlineData(false, 1100)]
    [InlineData(true, 1100)]
    public async Task SelectorsWrapWithinComposerBottomToolbar(bool dark, double width)
    {
        using var context = new LiveRoomFeedViewTestContext();
        context.ViewModel.SymbolGroups[0].Name = "很长的同传预设名称";
        context.DanmuConfigService.Config = context.DanmuConfigService.Config with
        {
            Colors = [new() { Value = 0xFFFFFF, Name = "白色" }, new() { Value = 0xFF0000, Name = "红色" }]
        };
        var view = new LiveRoomWorkspaceView(new(context.ViewModel, context.Room)) { DataContext = context.Room };
        var window = CreateWindow(context, view, width, dark);

        try
        {
            Assert.Single(context.DanmuConfigService.GetRequests);
            ShowWindow(window);
            await context.Room.DanmuConfigurationTask;
            ShowWindow(window);
            var composer = view.GetVisualDescendants().OfType<Border>()
                .Single(control => control.Classes.Contains("translateComposer"));
            var mark = view.FindControl<ComboBox>("ComposerMarkSelector")!;
            var color = view.FindControl<ComboBox>("DanmuColorSelector")!;
            var mode = view.FindControl<ComboBox>("DanmuModeSelector")!;
            var send = composer.GetVisualDescendants().OfType<Button>()
                .Single(control => control.Classes.Contains("translateComposerSendButton"));
            Assert.Single(context.DanmuConfigService.GetRequests);
            Assert.Empty(context.DanmuConfigService.SetRequests);
            Assert.Equal(0xFFFFFF, Assert.IsType<DanmuColorOption>(color.SelectedItem).Value);
            Assert.Equal(1, Assert.IsType<DanmuModeOption>(mode.SelectedItem).Value);

            var controls = new Control[] { mark, color, mode, send };
            var toolbar = composer.GetVisualDescendants().OfType<ComposerToolbarPanel>().Single();
            var input = view.FindControl<TextBox>("InputDraftTextBox")!;
            var toolbarTop = toolbar.TranslatePoint(default, composer)!.Value.Y;
            var toolbarBottom = toolbarTop + toolbar.Bounds.Height;
            var centers = controls.Select(control => control.TranslatePoint(
                new Point(0, control.Bounds.Height / 2), composer)!.Value.Y).ToArray();
            var rows = new List<double>();

            foreach (var center in centers)
            {
                if (!rows.Any(row => Math.Abs(center - row) <= 1))
                {
                    rows.Add(center);
                }
            }

            Assert.Equal(width == 760 ? 2 : 1, rows.Count);

            for (var index = 0; index < controls.Length; index++)
            {
                Assert.Contains(toolbar, controls[index].GetVisualAncestors());
                var left = controls[index].TranslatePoint(new Point(0, 0), composer)!.Value.X;
                var right = left + controls[index].Bounds.Width;
                var top = controls[index].TranslatePoint(default, composer)!.Value.Y;
                var bottom = top + controls[index].Bounds.Height;
                Assert.InRange(left, 11, composer.Bounds.Width - 11);
                Assert.InRange(right, left + 1, composer.Bounds.Width - 11);
                Assert.InRange(top, toolbarTop - 0.5, toolbarBottom);
                Assert.InRange(bottom, top + 1, toolbarBottom + 0.5);

                if (index > 0)
                {
                    var previous = controls[index - 1];
                    var previousEnd = previous.TranslatePoint(
                        new Point(previous.Bounds.Width, previous.Bounds.Height), composer)!.Value;
                    Assert.True(left >= previousEnd.X - 0.5 || top >= previousEnd.Y - 0.5,
                        "底部控件在同一行或换行后都不应重叠");
                }
            }

            var inputBottom = input.TranslatePoint(new Point(0, input.Bounds.Height), composer)!.Value.Y;
            var expectedBottom = composer.Bounds.Height - composer.Padding.Bottom - composer.BorderThickness.Bottom;
            Assert.True(input.Bounds.Height >= input.MinHeight - 0.5);
            Assert.True(inputBottom + 3.5 <= toolbarTop);
            Assert.InRange(toolbarBottom, expectedBottom - 0.5, expectedBottom + 0.5);
            color.SelectedItem = context.Room.DanmuColorOptions[1];
            await context.Room.DanmuConfigurationTask;
            mode.SelectedItem = context.Room.DanmuModeOptions[2];
            await context.Room.DanmuConfigurationTask;
            ShowWindow(window);
            Assert.Equal(new() { Color = 0xFF0000, Mode = 5 }, context.DanmuConfigService.SetRequests.Last().Options);
            Assert.Equal(context.Room.SelectedDanmuColor, color.SelectedItem);
            Assert.Equal(context.Room.SelectedDanmuMode, mode.SelectedItem);
            Assert.Equal(2, context.DanmuConfigService.SetRequests.Count);
            SaveFrame(window, $"danmu-config-{width}-{(dark ? "dark" : "light")}.png");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 读取失败后打开下拉框可重试，不增加输入框底部的状态行。
    /// </summary>
    [AvaloniaFact]
    public async Task OpeningFailedSelectorRetriesLoading()
    {
        using var context = new LiveRoomFeedViewTestContext();
        context.DanmuConfigService.Exception = new IOException("测试读取失败");
        var view = new LiveRoomWorkspaceView(new(context.ViewModel, context.Room)) { DataContext = context.Room };
        var window = CreateWindow(context, view, 1100, false);

        try
        {
            ShowWindow(window);
            await context.Room.DanmuConfigurationTask;
            var color = view.FindControl<ComboBox>("DanmuColorSelector")!;
            Assert.True(color.IsEnabled);
            Assert.Null(color.SelectedItem);
            Assert.Contains("测试读取失败", context.Room.DanmuConfigurationStatus);
            context.DanmuConfigService.Exception = null;
            color.IsDropDownOpen = true;
            await context.Room.DanmuConfigurationTask;
            ShowWindow(window);
            Assert.Equal(2, context.DanmuConfigService.GetRequests.Count);
            Assert.NotNull(color.SelectedItem);
            Assert.Empty(context.DanmuConfigService.SetRequests);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 面板迁移和隐藏保持当前房间的配置上下文，重新附着不重复读取。
    /// </summary>
    [AvaloniaFact]
    public async Task WorkbenchContextOwnsConfigurationAcrossPanelAttachment()
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = new LiveRoomWorkspaceView(new(context.ViewModel, context.Room)) { DataContext = context.Room };
        var window = CreateWindow(context, view, 1100, false);

        try
        {
            ShowWindow(window);
            await context.Room.DanmuConfigurationTask;
            window.Content = null;
            context.ViewModel.Accounts[0].Cookie = "session-b";
            await context.Room.DanmuConfigurationTask;
            Assert.Equal(2, context.DanmuConfigService.GetRequests.Count);
            Assert.NotEmpty(context.Room.DanmuColorOptions);
            window.Content = view;
            ShowWindow(window);
            await context.Room.DanmuConfigurationTask;
            Assert.Equal("session-b", context.DanmuConfigService.GetRequests[1].Cookie);
            Assert.Equal(2, context.DanmuConfigService.GetRequests.Count);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 新增房间先附着后注入依赖时，仅在 Configure 完成后读取一次。
    /// </summary>
    [AvaloniaFact]
    public async Task ConfigurationAfterAttachmentStartsLoading()
    {
        using var context = new LiveRoomFeedViewTestContext();
        var room = new Yohuke.DanmuNeo.ViewModels.Items.LiveRoomTabViewModel(new() { RoomId = "200" });
        var view = new LiveRoomWorkspaceView(new(context.ViewModel, room)) { DataContext = room };
        var window = CreateWindow(context, view, 1100, false);

        try
        {
            ShowWindow(window);
            Assert.Empty(context.DanmuConfigService.GetRequests);
            room.Configure(_ => context.ViewModel.Accounts[0], (_, _) => null, () => context.ViewModel.Settings,
                () => context.ViewModel.SymbolGroups[0], () => context.ViewModel.SymbolGroups, _ => [],
                new FakeDanmuSendService(), new FakeRoomAvatarService(), () => Task.CompletedTask,
                danmuConfigService: context.DanmuConfigService);
            await room.DanmuConfigurationTask;
            ShowWindow(window);
            Assert.Equal("200", Assert.Single(context.DanmuConfigService.GetRequests).RoomId);
            Assert.NotNull(view.FindControl<ComboBox>("DanmuColorSelector")!.SelectedItem);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 复用 View 切换房间会解绑旧身份，Cookie 修改只刷新新房间。
    /// </summary>
    [AvaloniaFact]
    public async Task DataContextSwitchDetachesOldConfiguration()
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = new LiveRoomWorkspaceView(new(context.ViewModel, context.Room)) { DataContext = context.Room };
        var window = CreateWindow(context, view, 1100, false);

        try
        {
            ShowWindow(window);
            await context.Room.DanmuConfigurationTask;
            context.ViewModel.NewRoomId = "200";
            await context.ViewModel.AddRoomAsync();
            var newRoom = context.ViewModel.SelectedWorkspace!.SelectedRoom!;
            view.DataContext = newRoom;
            await newRoom.DanmuConfigurationTask;
            Assert.Empty(context.Room.DanmuColorOptions);
            Assert.Equal("200", context.DanmuConfigService.GetRequests[1].RoomId);
            context.ViewModel.Accounts[0].Cookie = "session-b";
            await newRoom.DanmuConfigurationTask;
            Assert.Equal(3, context.DanmuConfigService.GetRequests.Count);
            Assert.Equal(("200", "session-b"), context.DanmuConfigService.GetRequests[2]);
            Assert.Empty(context.DanmuConfigService.SetRequests);
        }
        finally
        {
            window.Close();
        }
    }

    private static Window CreateWindow(LiveRoomFeedViewTestContext context, LiveRoomWorkspaceView view,
        double width, bool dark)
    {
        return new()
        {
            Width = width, Height = 680, Content = view, DataContext = context.ViewModel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
    }

    private static void ShowWindow(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void SaveFrame(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("YOHUKE_FEED_QA_OUTPUT");

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(Path.Combine(directory, name));
        }
    }
}
