using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 使用实际界面绑定、弹出菜单和鼠标点击验证弹幕功能。
/// </summary>
public class LiveRoomFeedViewTests
{
    /// <summary>
    /// 正式列表在两种主题与布局下区分普通粉丝牌和三个大航海档位的边缘。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void MedalEdgesDistinguishAllGuardTiers(bool compact, bool dark)
    {
        using var context = new LiveRoomFeedViewTestContext();
        context.ViewModel.IsCompactDanmuDisplay = compact;
        context.Room.DanmuItems.Clear();

        foreach (var level in new[] { 0, 3, 2, 1 })
        {
            context.Room.AddDanmu(new()
            {
                RoomId = "100", Uid = 42 + level, UserName = $"档位{level}用户", Content = "边缘颜色样本",
                MedalName = "星光", MedalLevel = 25, MedalGuardLevel = level
            });
        }

        var view = new LiveRoomWorkspaceView { DataContext = context.Room };
        var window = CreateWindow(context, view);
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        try
        {
            ShowWindow(window);
            var badges = view.GetVisualDescendants().OfType<Border>()
                .Where(control => control.Classes.Contains("danmuMedalBadge") && control.IsEffectivelyVisible).ToList();
            Assert.Equal(4, badges.Count);
            Assert.Equal(new[] { 0, 1, 2, 3 }, badges
                .Select(control => Assert.IsType<DanmuFeedItem>(control.DataContext).MedalGuardLevel).Order());

            foreach (var badge in badges)
            {
                var item = Assert.IsType<DanmuFeedItem>(badge.DataContext);
                Assert.Equal(item.MedalBorderColor, Assert.IsAssignableFrom<ISolidColorBrush>(badge.BorderBrush).Color);

                if (item.MedalGuardLevel != 0)
                {
                    Assert.True(badge.BorderThickness.Left > 0);
                }
            }

            var directory = Environment.GetEnvironmentVariable("YOHUKE_FEED_QA_OUTPUT");

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(directory,
                    $"medal-{(compact ? "compact" : "regular")}-{(dark ? "dark" : "light")}.png"));
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 普通和紧凑模式均展示粉丝牌等级和上舰档位，无牌用户不占用徽章位置。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MedalAndGuardTierRemainVisible(bool compact)
    {
        using var context = new LiveRoomFeedViewTestContext();
        context.ViewModel.IsCompactDanmuDisplay = compact;
        var view = new LiveRoomWorkspaceView { DataContext = context.Room };
        var window = CreateWindow(context, view);

        try
        {
            ShowWindow(window);
            var badges = view.GetVisualDescendants().OfType<Border>()
                .Where(control => control.Classes.Contains("danmuMedalBadge")).ToList();
            var badge = Assert.Single(badges, control => control.IsVisible);
            var medal = badge.GetVisualDescendants().OfType<TextBlock>()
                .Single(control => control.Classes.Contains("danmuMedalName"));
            var level = badge.GetVisualDescendants().OfType<TextBlock>()
                .Single(control => control.Classes.Contains("danmuMedalLevel"));
            Assert.Equal("夜更", medal.Text);
            Assert.Equal("25", level.Text);
            Assert.True(medal.IsEffectivelyVisible);
            Assert.True(medal.Bounds.Width > 0 && medal.Bounds.Height > 0);
            Assert.Single(badges, control => !control.IsVisible);
            var danmu = FindMedalDanmuButton(view);
            var name = danmu.GetVisualDescendants().OfType<TextBlock>()
                .Single(control => control.Classes.Contains("danmuFeedUser"));
            var badgePosition = badge.TranslatePoint(default, danmu)!.Value;
            var namePosition = name.TranslatePoint(default, danmu)!.Value;
            Assert.True(badgePosition.X + badge.Bounds.Width <= namePosition.X);
            Assert.InRange(badgePosition.Y + badge.Bounds.Height / 2,
                namePosition.Y + name.Bounds.Height / 2 - 0.5, namePosition.Y + name.Bounds.Height / 2 + 0.5);
            var guard = view.GetVisualDescendants().OfType<Button>().Single(control =>
                control.Classes.Contains("scFeedButton") &&
                control.DataContext is SuperChatItem { IsGuardPurchase: true });
            Assert.DoesNotContain(guard.GetVisualDescendants().OfType<TextBlock>(), control =>
                control.IsEffectivelyVisible && control.Classes.Contains("danmuFeedMeta"));
            Assert.DoesNotContain(guard.GetVisualDescendants(), control =>
                control.IsEffectivelyVisible && control.Classes.Contains("scFeedContent"));
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                control => control.Text == "舰长" && control.IsEffectivelyVisible);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                control => control.Text == "上舰用户" && control.IsEffectivelyVisible);
            var previewDirectory = Environment.GetEnvironmentVariable("YOHUKE_FEED_QA_OUTPUT");

            if (!string.IsNullOrWhiteSpace(previewDirectory))
            {
                Directory.CreateDirectory(previewDirectory);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(previewDirectory, compact ? "feed-compact.png" : "feed-regular.png"));
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 点击上舰卡片复制完整用户名并保留草稿，点击 SC 卡片仍可插入留言。
    /// </summary>
    [AvaloniaFact]
    public async Task GuardClickCopiesNameButSuperChatClickInsertsContent()
    {
        using var context = new LiveRoomFeedViewTestContext();
        var view = new LiveRoomWorkspaceView { DataContext = context.Room };
        var window = CreateWindow(context, view);

        try
        {
            ShowWindow(window);
            var clipboard = window.Clipboard!;
            await clipboard.SetTextAsync("原剪贴板内容");
            var buttons = view.GetVisualDescendants().OfType<Button>()
                .Where(control => control.Classes.Contains("scFeedButton")).ToList();
            Click(buttons.Single(control => control.DataContext is SuperChatItem
            {
                Kind: LivePaidEventKind.GuardPurchase
            }));
            Assert.Empty(context.Room.InputDraft);
            Assert.Equal("上舰用户", await clipboard.TryGetTextAsync());
            Click(buttons.Single(control => control.DataContext is SuperChatItem
            {
                Kind: LivePaidEventKind.SuperChat
            }));
            Assert.Contains("感谢主播", context.Room.InputDraft);
            Assert.Equal("上舰用户", await clipboard.TryGetTextAsync());
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 权限查询完成后菜单可点击，实际点击会再次查询权限并禁言目标 UID。
    /// </summary>
    [AvaloniaFact]
    public async Task MuteMenuWaitsForPermissionAndRechecksOnClick()
    {
        using var context = new LiveRoomFeedViewTestContext();
        var permission = new TaskCompletionSource<RoomModerationPermission>();
        context.ModerationService.RespondAsync =
            index => index == 1 ? permission.Task : Task.FromResult(CreatePermission());
        var view = new LiveRoomWorkspaceView { DataContext = context.Room };
        var window = CreateWindow(context, view);
        ContextMenu? menu = null;

        try
        {
            ShowWindow(window);
            var button = FindMedalDanmuButton(view);
            var item = Assert.IsType<DanmuFeedItem>(button.DataContext);
            menu = view.CreateDanmuContextMenu(item, context.Room);
            var muteItem = menu.Items.OfType<MenuItem>().Last();
            menu.Open(button);
            Dispatcher.UIThread.RunJobs();
            Assert.False(muteItem.IsEffectivelyEnabled);
            Assert.Equal(1, context.ModerationService.PermissionRequestCount);
            permission.SetResult(CreatePermission());
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(muteItem.IsEffectivelyEnabled);
            Assert.Same(context.Room.MuteDanmuCommand, muteItem.Command);
            Click(muteItem);
            var execution = context.Room.MuteDanmuCommand.ExecutionTask;
            Assert.NotNull(execution);
            await execution;

            Assert.Equal(2, context.ModerationService.PermissionRequestCount);
            Assert.Equal((100L, 30L, context.ViewModel.State.Accounts[0].Cookie),
                Assert.Single(context.ModerationService.MuteRequests));
        }
        finally
        {
            menu?.Close();
            window.Close();
        }
    }

    /// <summary>
    /// 权限查询期间关闭菜单会取消查询，迟到的结果不能启用该菜单。
    /// </summary>
    [AvaloniaFact]
    public void ClosingPendingMenuCancelsPermissionQuery()
    {
        using var context = new LiveRoomFeedViewTestContext();
        var permission = new TaskCompletionSource<RoomModerationPermission>();
        context.ModerationService.RespondAsync = _ => permission.Task;
        var view = new LiveRoomWorkspaceView { DataContext = context.Room };
        var window = CreateWindow(context, view);
        ContextMenu? menu = null;

        try
        {
            ShowWindow(window);
            var button = FindMedalDanmuButton(view);
            menu = view.CreateDanmuContextMenu(Assert.IsType<DanmuFeedItem>(button.DataContext), context.Room);
            var muteItem = menu.Items.OfType<MenuItem>().Last();
            menu.Open(button);
            Dispatcher.UIThread.RunJobs();
            menu.Close();
            Assert.True(Assert.Single(context.ModerationService.PermissionTokens).IsCancellationRequested);
            permission.SetResult(CreatePermission());
            Dispatcher.UIThread.RunJobs();

            Assert.False(muteItem.IsEnabled);
            Assert.False(context.Room.MuteDanmuCommand.CanExecute(button.DataContext));
            Assert.Empty(context.ModerationService.MuteRequests);
        }
        finally
        {
            menu?.Close();
            window.Close();
        }
    }

    private static Window CreateWindow(LiveRoomFeedViewTestContext context, LiveRoomWorkspaceView view)
    {
        return new() { Width = 1100, Height = 800, DataContext = context.ViewModel, Content = view };
    }

    private static void ShowWindow(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static Button FindMedalDanmuButton(LiveRoomWorkspaceView view)
    {
        return view.GetVisualDescendants().OfType<Button>().Single(control =>
            control.Classes.Contains("danmuFeedButton") && control.DataContext is DanmuFeedItem { UserUid: "30" });
    }

    private static void Click(Control control)
    {
        var topLevel = TopLevel.GetTopLevel(control)!;
        topLevel.UpdateLayout();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), topLevel)!
            .Value;
        topLevel.MouseMove(point);
        topLevel.MouseDown(point, MouseButton.Left);
        topLevel.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static RoomModerationPermission CreatePermission()
    {
        return new() { RoomId = 100, AccountUid = 10, OwnerUid = 20, IsRoomAdmin = true };
    }
}
