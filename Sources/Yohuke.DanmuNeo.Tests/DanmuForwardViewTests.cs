using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 转发下拉选项与后台监听的页面切换回归测试。
/// </summary>
public class DanmuForwardViewTests
{
    /// <summary>
    /// 切换工具标签或直播间时保留规则选项，并继续使用指定账号和符号发送。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LeavingForwardPageKeepsSelectionsAndRunningSession(bool switchRoom, bool reuseView)
    {
        using var context = new DanmuForwardViewTestContext();
        var view = new LiveRoomWorkspaceView(new(context.ViewModel, context.Room)) { DataContext = context.Room };
        var window = new Window { DataContext = context.ViewModel, Content = view, Width = 1200, Height = 800 };

        try
        {
            window.Show();
            PumpLayout(window);
            var tabs = view.GetVisualDescendants().OfType<TabControl>()
                .Single(control => control.Classes.Contains("workspaceToolTabs"));
            var forwardTab = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Header, "转发"));
            tabs.SelectedItem = forwardTab;
            PumpLayout(window);
            var rule = context.Room.ForwardRules[0];
            AssertSelections(view, context);
            rule.IsEnabled = true;
            await context.SocketFactory.Socket.StartedTask.WaitAsync(TimeSpan.FromSeconds(2));

            for (var index = 0; index < 3; index++)
            {
                if (switchRoom)
                {
                    if (reuseView)
                    {
                        view.DataContext = context.Source;
                    }
                    else
                    {
                        window.Content = new LiveRoomWorkspaceView(new(context.ViewModel, context.Source)) { DataContext = context.Source };
                    }
                }
                else
                {
                    tabs.SelectedIndex = index;
                }

                PumpLayout(window);
                Assert.Equal("workspace|source-room", rule.SourceRoomKey);
                Assert.Equal("forward-account", rule.AccountOverrideId);
                Assert.Equal("forward-group", rule.MarkSymbolGroupId);
                Assert.True(rule.IsEnabled);
                Assert.False(context.SocketFactory.Socket.DisposedTask.IsCompleted);
                Assert.Single(context.SocketFactory.Requests);
                context.SocketFactory.Socket.EmitDanmu(new()
                {
                    RoomId = "100", Uid = 42, UserName = "发送者", Content = $"后台转发{index}"
                });
                PumpLayout(window);
                Assert.Equal($"[后台转发{index}]", context.SendService.SentMessages[index]);
                Assert.Same(context.ViewModel.Accounts[1], context.SendService.SentAccounts[index]);

                if (switchRoom)
                {
                    if (reuseView)
                    {
                        view.DataContext = context.Room;
                    }
                    else
                    {
                        window.Content = view;
                    }
                }
                else
                {
                    tabs.SelectedItem = forwardTab;
                }

                PumpLayout(window);
                AssertSelections(view, context);
            }

            await context.ViewModel.SaveAsync();
            var savedRule = context.StateService.Load().Workspaces[0].LiveRooms
                .Single(room => room.Id == "target-room").ForwardRules[0];
            Assert.Equal("source-room", savedRule.SourceRoomStateId);
            Assert.Equal("forward-account", savedRule.AccountOverrideId);
            Assert.Equal("forward-group", savedRule.MarkSymbolGroupId);
        }
        finally
        {
            context.Room.StopForwarding();
            window.Close();
            PumpLayout(window);

            if (context.SocketFactory.Socket.StartedTask.IsCompleted)
            {
                await context.SocketFactory.Socket.DisposedTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
    }

    /// <summary>
    /// 新增规则具有完整下拉选项，仍可选择指定账号或恢复目标直播间账号。
    /// </summary>
    [AvaloniaFact]
    public async Task AddedRuleCanSelectAccountAndSymbolGroup()
    {
        using var context = new DanmuForwardViewTestContext();
        var view = new LiveRoomWorkspaceView(new(context.ViewModel, context.Room)) { DataContext = context.Room };
        var window = new Window { DataContext = context.ViewModel, Content = view, Width = 1200, Height = 800 };

        try
        {
            window.Show();
            PumpLayout(window);
            var tabs = view.GetVisualDescendants().OfType<TabControl>()
                .Single(control => control.Classes.Contains("workspaceToolTabs"));
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Header, "转发"));
            await context.Room.AddForwardRuleAsync();
            PumpLayout(window);
            var rule = context.Room.ForwardRules[1];
            var selectors = view.GetVisualDescendants().OfType<ComboBox>()
                .Where(control => ReferenceEquals(control.DataContext, rule)).ToList();
            Assert.Equal(3, selectors.Count);
            Assert.Equal("workspace|source-room", selectors[0].SelectedValue);
            Assert.Equal(3, selectors[1].ItemCount);
            Assert.Equal(2, selectors[2].ItemCount);
            selectors[1].SelectedItem = context.ViewModel.ForwardAccountOptions[2];
            selectors[2].SelectedItem = context.ViewModel.SymbolGroups[1];
            Assert.Equal("forward-account", rule.AccountOverrideId);
            Assert.Equal("forward-group", rule.MarkSymbolGroupId);
            selectors[1].SelectedItem = context.ViewModel.ForwardAccountOptions[0];
            selectors[2].SelectedItem = context.ViewModel.SymbolGroups[0];
            Assert.Equal("", rule.AccountOverrideId);
            Assert.Equal("default-group", rule.MarkSymbolGroupId);
            await context.ViewModel.SaveAsync();
        }
        finally
        {
            window.Close();
            PumpLayout(window);
        }
    }

    /// <summary>
    /// 用户名在 UID 右侧显示，长名字在窄面板内省略且保留完整提示。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(600)]
    [InlineData(1200)]
    public async Task UserNameAppearsBesideUidWithoutOverflow(double width)
    {
        const string USER_NAME = "很长的同传用户名用于检查窄面板的省略显示";
        var service = new FakeBilibiliUserNameService { RespondAsync = (_, _) => Task.FromResult(USER_NAME) };
        using var context = new DanmuForwardViewTestContext(service);
        var view = new LiveRoomWorkspaceView(new(context.ViewModel, context.Room)) { DataContext = context.Room };
        var window = new Window { DataContext = context.ViewModel, Content = view, Width = width, Height = 800 };

        try
        {
            window.Show();
            PumpLayout(window);
            SelectForwardTab(view);
            await context.Room.ForwardRules[0].UserNameLookupTask;
            PumpLayout(window);
            var name = view.GetVisualDescendants().OfType<TextBlock>()
                .Single(control => control.Name == "ForwardSenderUserNameText");
            var row = Assert.IsType<Grid>(name.GetVisualParent());
            var input = row.Children.OfType<TextBox>().Single();

            Assert.Equal(USER_NAME, name.Text);
            Assert.Equal(USER_NAME, ToolTip.GetTip(name));
            Assert.InRange(name.Bounds.Width, 1, 120);
            Assert.True(input.Bounds.Width > 0);
            Assert.True(name.Bounds.Left >= input.Bounds.Right);
            Assert.True(name.Bounds.Right <= row.Bounds.Width + 0.1);
            Assert.Equal([42L], service.RequestedUids);
        }
        finally
        {
            window.Close();
            PumpLayout(window);
        }
    }

    /// <summary>
    /// 新增账号选中后改名、增删其他账号，不丢失选项或重启转发连接。
    /// </summary>
    [AvaloniaFact]
    public async Task RenamingNewAccountUpdatesSelectedTextAndKeepsForwarding()
    {
        using var context = new DanmuForwardViewTestContext();
        await context.ViewModel.AddAccountAsync();
        var account = context.ViewModel.Accounts.Last();
        account.Cookie = "new-forward-cookie";
        var option = context.ViewModel.ForwardAccountOptions.Single(item => item.AccountId == account.Id);
        var view = new LiveRoomWorkspaceView(new(context.ViewModel, context.Room)) { DataContext = context.Room };
        var window = new Window { DataContext = context.ViewModel, Content = view, Width = 1200, Height = 800 };

        try
        {
            window.Show();
            PumpLayout(window);
            SelectForwardTab(view);
            PumpLayout(window);
            var rule = context.Room.ForwardRules[0];
            var selector = view.GetVisualDescendants().OfType<ComboBox>()
                .Single(control => ReferenceEquals(control.DataContext, rule) &&
                                   ReferenceEquals(control.ItemsSource, context.ViewModel.ForwardAccountOptions));
            selector.SelectedItem = option;
            rule.IsEnabled = true;
            await context.SocketFactory.Socket.StartedTask.WaitAsync(TimeSpan.FromSeconds(2));
            account.Name = "新账号修改后的名称";
            await rule.UserNameLookupTask;
            PumpLayout(window);

            Assert.Contains(selector.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == account.Name);
            await context.ViewModel.AddAccountAsync();
            await context.ViewModel.DeleteAccountAsync(context.ViewModel.Accounts.Last());
            PumpLayout(window);

            Assert.Same(option, selector.SelectedItem);
            Assert.Equal(account.Id, rule.AccountOverrideId);
            Assert.Single(context.SocketFactory.Requests);
            Assert.False(context.SocketFactory.Socket.DisposedTask.IsCompleted);
            context.SocketFactory.Socket.EmitDanmu(new() { RoomId = "100", Uid = 42, Content = "改名后仍然转发" });
            PumpLayout(window);
            Assert.Equal("[改名后仍然转发]", Assert.Single(context.SendService.SentMessages));
            Assert.Same(account, Assert.Single(context.SendService.SentAccounts));
        }
        finally
        {
            context.Room.StopForwarding();
            window.Close();
            PumpLayout(window);

            if (context.SocketFactory.Socket.StartedTask.IsCompleted)
            {
                await context.SocketFactory.Socket.DisposedTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }
    }

    private static void SelectForwardTab(LiveRoomWorkspaceView view)
    {
        var tabs = view.GetVisualDescendants().OfType<TabControl>()
            .Single(control => control.Classes.Contains("workspaceToolTabs"));
        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => Equals(tab.Header, "转发"));
    }

    private static void AssertSelections(LiveRoomWorkspaceView view, DanmuForwardViewTestContext context)
    {
        var selectors = view.GetVisualDescendants().OfType<ComboBox>()
            .Where(control => ReferenceEquals(control.DataContext, context.Room.ForwardRules[0])).ToList();
        Assert.Equal(3, selectors.Count);
        Assert.Equal(context.Room.ForwardSourceRooms.Count, selectors[0].ItemCount);
        Assert.Equal(context.ViewModel.ForwardAccountOptions.Count, selectors[1].ItemCount);
        Assert.Equal(context.ViewModel.SymbolGroups.Count, selectors[2].ItemCount);
        Assert.Same(context.Room.ForwardSourceRooms[0], selectors[0].SelectedItem);
        Assert.Equal("workspace|source-room", selectors[0].SelectedValue);
        Assert.Same(context.ViewModel.ForwardAccountOptions[2], selectors[1].SelectedItem);
        Assert.Equal("forward-account", selectors[1].SelectedValue);
        Assert.Same(context.ViewModel.SymbolGroups[1], selectors[2].SelectedItem);
        Assert.Equal("forward-group", selectors[2].SelectedValue);
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }
}
