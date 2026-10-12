using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 使用正式输入区验证状态日志移除、紧凑布局和失败描边。
/// </summary>
public class DraftComposerViewTests
{
    /// <summary>
    /// 普通、悬停和输入焦点状态均没有描边，发送状态不会产生底部日志或空白。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComposerHasNoNormalOutlineOrStatusFooter(bool dark)
    {
        using var context = new MarkGroupCycleTestContext();
        var room = context.ViewModel.Workspaces[0].Rooms[0];
        var view = new LiveRoomWorkspaceView { DataContext = room };
        var window = new Window
        {
            Width = 1100, Height = 680, Content = view, DataContext = context.ViewModel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };

        try
        {
            ShowWindow(window);
            var composer = view.GetVisualDescendants().OfType<Border>()
                .Single(control => control.Classes.Contains("translateComposer"));
            var input = view.FindControl<TextBox>("InputDraftTextBox")!;
            AssertNoVisibleOutline(composer);
            input.Focus();
            var position = input.TranslatePoint(new Point(8, 8), window)!.Value;
            window.MouseMove(position);
            Dispatcher.UIThread.RunJobs();
            AssertNoVisibleOutline(composer);
            Assert.Equal(new Thickness(0), input.BorderThickness);

            var height = composer.Bounds.Height;
            room.DraftSendStatus = "发送失败：测试接口拒绝，这一行不应显示";
            ShowWindow(window);
            Assert.DoesNotContain(composer.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == room.DraftSendStatus);
            Assert.Equal(height, composer.Bounds.Height);
            Assert.InRange(height, 70, 112);
            var send = composer.GetVisualDescendants().OfType<Button>()
                .Single(control => control.Classes.Contains("translateComposerSendButton"));
            var bottom = send.TranslatePoint(new Point(0, send.Bounds.Height), composer)!.Value.Y;
            Assert.InRange(composer.Bounds.Height - bottom, 8, 12);
            SaveFrame(window, dark ? "composer-normal-dark.png" : "composer-normal-light.png");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 明确拒绝和网络失败时，即使输入保持焦点和悬停也显示红框，编辑后恢复。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureOutlineSurvivesFocusAndClearsOnEditing(bool dark)
    {
        using var context = new MarkGroupCycleTestContext();
        var sendService = new FakeDanmuSendService { IsAccepted = false };
        var room = new LiveRoomTabViewModel(new() { RoomId = "100", RoomName = "测试直播间" });
        room.Configure(_ => null, (_, _) => null, () => context.ViewModel.Settings,
            () => context.ViewModel.SelectedWorkspaceMarkGroup!, () => context.ViewModel.SymbolGroups,
            _ => [], sendService, new FakeRoomAvatarService(), () => Task.CompletedTask);
        var view = new LiveRoomWorkspaceView { DataContext = room };
        var window = new Window
        {
            Width = 1100, Height = 680, Content = view, DataContext = context.ViewModel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };

        try
        {
            ShowWindow(window);
            var composer = view.GetVisualDescendants().OfType<Border>()
                .Single(control => control.Classes.Contains("translateComposer"));
            var input = view.FindControl<TextBox>("InputDraftTextBox")!;
            room.InputDraft = "测试同传内容";
            input.Focus();
            window.MouseMove(input.TranslatePoint(new Point(8, 8), window)!.Value);
            await room.SendDraftAsync();
            ShowWindow(window);

            Assert.True(composer.BorderThickness.Left > 0);
            Assert.Equal(Color.Parse("#EF4444"), Assert.IsAssignableFrom<ISolidColorBrush>(composer.BorderBrush).Color);
            Assert.Equal(new Thickness(0), input.BorderThickness);
            SaveFrame(window, dark ? "composer-rejected-dark.png" : "composer-rejected-light.png");

            input.Text = "修改内容";
            ShowWindow(window);
            Assert.False(room.IsDraftSendFailed);
            AssertNoVisibleOutline(composer);

            sendService.FailureApiCode = null;
            await room.SendDraftAsync();
            ShowWindow(window);
            Assert.Equal(Color.Parse("#EF4444"), Assert.IsAssignableFrom<ISolidColorBrush>(composer.BorderBrush).Color);
            Assert.Equal("修改内容", input.Text);

            room.InputDraft = new string('中', 150);
            ShowWindow(window);
            var text = input.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().Single();
            Assert.True(text.TextLayout.TextLines.Count > 1);
            Assert.True(input.Bounds.Height >= text.TextLayout.Height);
        }
        finally
        {
            window.Close();
            room.StopListening();
        }
    }

    /// <summary>
    /// 回显验证超时保留草稿并显示红框，重试确认成功后清空草稿并恢复正常边框。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EchoTimeoutHighlightsAndConfirmedRetryClearsDraft(bool dark)
    {
        using var context = new MarkGroupCycleTestContext();
        var sendService = new FakeDanmuSendService();
        var socketFactory = new FakeLiveDanmuSocketFactory();
        socketFactory.Socket.KeepRunning = true;
        var account = new BilibiliAccount { Cookie = "DedeUserID=42" };
        var room = new LiveRoomTabViewModel(new() { RoomId = "100", RoomName = "测试直播间" });
        room.Configure(_ => account, (_, _) => null, () => context.ViewModel.Settings,
            () => context.ViewModel.SelectedWorkspaceMarkGroup!, () => context.ViewModel.SymbolGroups,
            _ => [], sendService, new FakeRoomAvatarService(), () => Task.CompletedTask,
            liveDanmuSocketFactory: socketFactory);
        var echoTimedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var echoConfirmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        room.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(room.DraftSendStatus))
            {
                return;
            }

            if (room.DraftSendStatus == "接口已接受，未确认显示")
            {
                echoTimedOut.TrySetResult();
            }
            else if (room.DraftSendStatus == "已在弹幕流确认")
            {
                echoConfirmed.TrySetResult();
            }
        };
        var view = new LiveRoomWorkspaceView { DataContext = room };
        var window = new Window
        {
            Width = 1100, Height = 680, Content = view, DataContext = context.ViewModel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };

        try
        {
            ShowWindow(window);
            var composer = view.GetVisualDescendants().OfType<Border>()
                .Single(control => control.Classes.Contains("translateComposer"));
            var input = view.FindControl<TextBox>("InputDraftTextBox")!;
            room.InputDraft = "回显核验内容";
            input.Focus();
            window.MouseMove(input.TranslatePoint(new Point(8, 8), window)!.Value);
            room.StartListening();
            await socketFactory.Socket.StartedTask.WaitAsync(TimeSpan.FromSeconds(1));
            await room.SendDraftAsync();
            ShowWindow(window);
            Assert.Equal("回显核验内容", input.Text);
            AssertNoVisibleOutline(composer);

            await echoTimedOut.Task.WaitAsync(TimeSpan.FromSeconds(7));
            ShowWindow(window);
            Assert.True(room.IsDraftSendFailed);
            Assert.Equal("回显核验内容", input.Text);
            Assert.Equal(Color.Parse("#EF4444"), Assert.IsAssignableFrom<ISolidColorBrush>(composer.BorderBrush).Color);
            SaveFrame(window, dark ? "composer-echo-timeout-dark.png" : "composer-echo-timeout-light.png");

            await room.SendDraftAsync();
            socketFactory.Socket.EmitDanmu(new() { Uid = 42, Content = "【回显核验内容】" });
            await echoConfirmed.Task.WaitAsync(TimeSpan.FromSeconds(1));
            ShowWindow(window);
            Assert.False(room.IsDraftSendFailed);
            Assert.Equal("", input.Text);
            AssertNoVisibleOutline(composer);
        }
        finally
        {
            window.Close();
            room.StopListening();
        }
    }

    private static void AssertNoVisibleOutline(Border composer)
    {
        Assert.True(composer.BorderThickness == new Thickness(0) ||
                    composer.BorderBrush is ISolidColorBrush { Color.A: 0 });
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
