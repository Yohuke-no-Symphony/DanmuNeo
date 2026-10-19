using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证账号粉丝牌工具的独立上下文与可见生命周期。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public class AccountMedalsPanelViewTests
{
    /// <summary>真实鼠标单击即可佩戴和摘下，滞后的牌墙不会覆盖徽章与选择。</summary>
    [AvaloniaFact]
    public async Task OnePointerClickWearsAndTakesOffWithStaleWall()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var log = new AppLogService();
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        var account = MedalSwitchCoordinatorTests.Account();
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        var service = new FakeMedalService();
        var old = service.Collection;
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask,
            () => TimeSpan.FromSeconds(3), log);
        using var viewModel = new AccountMedalsViewModel(new([account]), coordinator, log);
        viewModel.UpdateCurrentAccount(account);
        using var panel = new AccountMedalsPanelView(context, viewModel);
        var window = new Window { Width = 420, Height = 560, Content = panel };
        try
        {
            window.Show();
            await viewModel.ActivateAsync();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var list = panel.FindControl<ListBox>("MedalList")!;
            var row = list.GetRealizedContainers().OfType<ListBoxItem>()
                .Single(item => ReferenceEquals(item.DataContext, viewModel.Medals.Single(medal => medal.Id == 11)));
            Click(window, row);
            Assert.Equal(11, viewModel.SelectedMedal?.Id);
            service.ReadResponse = _ => old;
            Click(window, panel.FindControl<Button>("MedalWear")!);
            await viewModel.PendingOperation;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(["wear:11"], service.Writes);
            Assert.True(viewModel.SelectedMedal?.IsWearing);
            Assert.Single(list.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("medalWearingBadge") && border.IsEffectivelyVisible);
            Click(window, panel.FindControl<Button>("MedalTakeOff")!);
            await viewModel.PendingOperation;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(["wear:11", "off:11"], service.Writes);
            Assert.DoesNotContain(list.GetVisualDescendants().OfType<Border>(),
                border => border.Classes.Contains("medalWearingBadge") && border.IsEffectivelyVisible);
            Assert.Equal(11, viewModel.SelectedMedal?.Id);
            var follow = panel.FindControl<ToggleButton>("MedalFollowAccount")!;
            Click(window, follow);
            Assert.False(viewModel.FollowCurrentAccount);
            Assert.True(panel.FindControl<ComboBox>("MedalAccountChoice")!.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>无房间也可选择账号管理；隐藏和构造不触发读取，关闭视图保留应用级模型。</summary>
    [AvaloniaFact]
    public async Task PanelWorksWithoutRoomAndOnlyReadsWhileVisible()
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        fixture.ViewModel.SelectedWorkspace!.SelectedRoom = null;
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var log = new AppLogService();
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        var account = MedalSwitchCoordinatorTests.Account();
        account.Name = "离线测试账号";
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        var service = new FakeMedalService();
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask, () => TimeSpan.FromSeconds(3), log);
        using var viewModel = new AccountMedalsViewModel(new([account]), coordinator, log);
        viewModel.FollowCurrentAccount = false;
        viewModel.SelectedAccount = account;
        using var panel = new AccountMedalsPanelView(context, viewModel);
        var host = new Border { Child = panel, IsVisible = false };
        var window = new Window { Width = 320, Height = 560, Content = host };
        try
        {
            Assert.Equal(0, service.ReadCount);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, service.ReadCount);
            host.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            await viewModel.PendingOperation;
            Assert.True(panel.IsEnabled);
            Assert.Same(viewModel, panel.DataContext);
            Assert.True(panel.FindControl<ComboBox>("MedalAccountChoice")!.IsEnabled);
            Assert.Equal(3, panel.FindControl<ListBox>("MedalList")!.ItemCount);
            Assert.Equal(1, service.ReadCount);
            fixture.ViewModel.SelectedWorkspace.SelectedRoom = fixture.Room;
            Assert.Same(viewModel, panel.DataContext);
            fixture.ViewModel.SelectedWorkspace.SelectedRoom = null;
            Assert.True(panel.IsEnabled);
            host.IsVisible = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, viewModel.Medals.Count);
        }
        finally
        {
            window.Close();
        }
        await viewModel.ActivateAsync();
        Assert.Equal(3, viewModel.Medals.Count);
    }

    private static void Click(Window window, Control control)
    {
        var center = control.TranslatePoint(new(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(center);
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
