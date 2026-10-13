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
        var view = new LiveRoomWorkspaceView { DataContext = context.Room };
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
                        window.Content = new LiveRoomWorkspaceView { DataContext = context.Source };
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
        var view = new LiveRoomWorkspaceView { DataContext = context.Room };
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
