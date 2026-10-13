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
    /// 窄窗口与常规窗口都在原下拉框同一行显示配置，绑定改变立即保存。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, 760)]
    [InlineData(true, 760)]
    [InlineData(false, 1100)]
    [InlineData(true, 1100)]
    public async Task SelectorsStayOnComposerBottomRow(bool dark, double width)
    {
        using var context = new LiveRoomFeedViewTestContext();
        context.ViewModel.SymbolGroups[0].Name = "很长的同传预设名称";
        context.DanmuConfigService.Config = context.DanmuConfigService.Config with
        {
            Colors = [new() { Value = 0xFFFFFF, Name = "白色" }, new() { Value = 0xFF0000, Name = "红色" }]
        };
        var view = new LiveRoomWorkspaceView { DataContext = context.Room };
        var window = CreateWindow(context, view, width, dark);

        try
        {
            Assert.Empty(context.DanmuConfigService.GetRequests);
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
            var centers = controls.Select(control => control.TranslatePoint(
                new Point(0, control.Bounds.Height / 2), composer)!.Value.Y).ToArray();
            Assert.InRange(centers.Max() - centers.Min(), 0, 1);

            for (var index = 0; index < controls.Length; index++)
            {
                var left = controls[index].TranslatePoint(new Point(0, 0), composer)!.Value.X;
                var right = left + controls[index].Bounds.Width;
                Assert.InRange(left, 11, composer.Bounds.Width - 11);
                Assert.InRange(right, left + 1, composer.Bounds.Width - 11);

                if (index > 0)
                {
                    var previous = controls[index - 1];
                    var previousRight = previous.TranslatePoint(new Point(previous.Bounds.Width, 0), composer)!.Value.X;
                    Assert.True(left >= previousRight, "底部控件不应重叠");
                }
            }

            Assert.InRange(composer.Bounds.Height, 70, 112);
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
        var view = new LiveRoomWorkspaceView { DataContext = context.Room };
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
    /// View 关闭后不再观察账号，重新附着才读取最新配置。
    /// </summary>
    [AvaloniaFact]
    public async Task ViewAttachmentOwnsConfigurationLifetime()
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = new LiveRoomWorkspaceView { DataContext = context.Room };
        var window = CreateWindow(context, view, 1100, false);

        try
        {
            ShowWindow(window);
            await context.Room.DanmuConfigurationTask;
            window.Content = null;
            context.ViewModel.Accounts[0].Cookie = "session-b";
            Assert.Single(context.DanmuConfigService.GetRequests);
            Assert.Empty(context.Room.DanmuColorOptions);
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
        var view = new LiveRoomWorkspaceView { DataContext = room };
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
        var view = new LiveRoomWorkspaceView { DataContext = context.Room };
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
