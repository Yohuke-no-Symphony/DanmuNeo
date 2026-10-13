using System.Threading;
using Avalonia.Headless.XUnit;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证弹幕配置读取、保存与身份切换的异步边界。
/// </summary>
public class RoomDanmuConfigurationTests
{
    /// <summary>
    /// 构造与配置依赖时不发请求，首次激活从服务端读取且不触发保存。
    /// </summary>
    [AvaloniaFact]
    public async Task ActivationLoadsServerSelectionWithoutSaving()
    {
        var service = new FakeBilibiliDanmuConfigService
        {
            Config = new FakeBilibiliDanmuConfigService().Config with { Current = new() { Color = 0xFF0000, Mode = 5 } }
        };
        var account = new BilibiliAccount { Cookie = "session-a" };
        var room = CreateRoom(service, () => account);

        try
        {
            Assert.Empty(service.GetRequests);
            await room.ActivateDanmuConfigurationAsync();
            Assert.Equal(0xFF0000, room.SelectedDanmuColor?.Value);
            Assert.Equal(5, room.SelectedDanmuMode?.Value);
            Assert.Equal(("100", "session-a"), Assert.Single(service.GetRequests));
            Assert.Empty(service.SetRequests);
            Assert.True(room.CanSelectDanmuColor);
            await room.ActivateDanmuConfigurationAsync();
            Assert.Single(service.GetRequests);
        }
        finally
        {
            room.DeactivateDanmuConfiguration();
        }
    }

    /// <summary>
    /// 连续改变颜色和位置时顺序保存，后一次包含最新的完整选项。
    /// </summary>
    [AvaloniaFact]
    public async Task RapidSelectionChangesAreSerialized()
    {
        var service = new FakeBilibiliDanmuConfigService();
        var firstSave = new TaskCompletionSource<DanmuRoomConfig>();
        service.WriteAsync = (_, _, options, _) => service.SetRequests.Count == 1
            ? firstSave.Task
            : Task.FromResult(service.Config with { Current = options });
        var room = CreateRoom(service, () => new() { Id = "account-a", Cookie = "session-a" });

        try
        {
            await room.ActivateDanmuConfigurationAsync();
            room.SelectedDanmuColor = room.DanmuColorOptions[1];
            room.SelectedDanmuMode = room.DanmuModeOptions[2];
            Assert.Single(service.SetRequests);
            Assert.True(room.IsSavingDanmuConfiguration);
            firstSave.SetResult(service.Config with { Current = service.SetRequests[0].Options });
            await room.DanmuConfigurationTask;

            Assert.Equal(2, service.SetRequests.Count);
            Assert.Equal(new() { Color = 0xFF0000, Mode = 5 }, service.SetRequests[1].Options);
            Assert.Equal(5, room.SelectedDanmuMode?.Value);
            Assert.False(room.IsSavingDanmuConfiguration);
        }
        finally
        {
            room.DeactivateDanmuConfiguration();
        }
    }

    /// <summary>
    /// 保存拒绝后恢复服务端已确认的值，不自动重复保存。
    /// </summary>
    [AvaloniaFact]
    public async Task SaveFailureRestoresConfirmedSelection()
    {
        var service = new FakeBilibiliDanmuConfigService
        {
            WriteAsync = (_, _, _, _) => Task.FromException<DanmuRoomConfig>(new InvalidOperationException("权限不足"))
        };
        var account = new BilibiliAccount { Cookie = "session-a" };
        var room = CreateRoom(service, () => account);

        try
        {
            await room.ActivateDanmuConfigurationAsync();
            room.SelectedDanmuColor = room.DanmuColorOptions[1];
            await room.DanmuConfigurationTask;
            Assert.Equal(0xFFFFFF, room.SelectedDanmuColor?.Value);
            Assert.Equal(1, room.SelectedDanmuMode?.Value);
            Assert.Single(service.SetRequests);
            Assert.Contains("权限不足", room.DanmuConfigurationStatus);
            Assert.True(room.CanSelectDanmuColor);
        }
        finally
        {
            room.DeactivateDanmuConfiguration();
        }
    }

    /// <summary>
    /// 保存结果不确定时重新读取实际值，无法读取时清空旧值并允许重试。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UncertainSaveRecoversAuthoritativeConfig(bool recoveryFails)
    {
        var service = new FakeBilibiliDanmuConfigService();
        var account = new BilibiliAccount { Cookie = "session-a" };
        var room = CreateRoom(service, () => account);

        try
        {
            await room.ActivateDanmuConfigurationAsync();
            service.WriteAsync = (_, _, options, _) =>
            {
                service.Config = service.Config with { Current = options };
                service.Exception = recoveryFails ? new IOException("读取失败") : null;
                return Task.FromException<DanmuRoomConfig>(new IOException("连接中断"));
            };
            room.SelectedDanmuColor = room.DanmuColorOptions[1];
            await room.DanmuConfigurationTask;

            if (recoveryFails)
            {
                Assert.Null(room.SelectedDanmuColor);
                Assert.Empty(room.DanmuColorOptions);
                service.Exception = null;
                await room.RetryDanmuConfigurationAsync();
            }

            Assert.Equal(0xFF0000, room.SelectedDanmuColor?.Value);
            Assert.Single(service.SetRequests);
        }
        finally
        {
            room.DeactivateDanmuConfiguration();
        }
    }

    /// <summary>
    /// Cookie 原地修改时取消旧读取，即使旧服务忽略取消也不能覆盖新身份。
    /// </summary>
    [AvaloniaFact]
    public async Task CookieChangeCancelsAndDiscardsOldRead()
    {
        var service = new FakeBilibiliDanmuConfigService();
        var oldRead = new TaskCompletionSource<DanmuRoomConfig>();
        var oldToken = CancellationToken.None;
        service.ReadAsync = (_, cookie, token, _) =>
        {
            if (cookie == "session-a")
            {
                oldToken = token;
                return oldRead.Task;
            }

            return Task.FromResult(service.Config with { Current = new() { Color = 0xFF0000, Mode = 5 } });
        };
        var account = new BilibiliAccount { Cookie = "session-a" };
        var room = CreateRoom(service, () => account);

        try
        {
            var oldTask = room.ActivateDanmuConfigurationAsync();
            account.Cookie = "session-b";
            await room.DanmuConfigurationTask;
            Assert.True(oldToken.IsCancellationRequested);
            oldRead.SetResult(service.Config);
            await oldTask;
            Assert.Equal(0xFF0000, room.SelectedDanmuColor?.Value);
            Assert.Equal(5, room.SelectedDanmuMode?.Value);
            Assert.Equal("session-b", service.GetRequests[1].Cookie);
            Assert.Empty(service.SetRequests);
        }
        finally
        {
            room.DeactivateDanmuConfiguration();
        }
    }

    /// <summary>
    /// 房间或账号切换后的旧保存失败不能回退新的选择。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContextChangeDiscardsOldSave(bool changeRoom)
    {
        var service = new FakeBilibiliDanmuConfigService();
        var oldSave = new TaskCompletionSource<DanmuRoomConfig>();
        var oldToken = CancellationToken.None;
        service.WriteAsync = (_, _, _, token) =>
        {
            oldToken = token;
            return oldSave.Task;
        };
        var account = new BilibiliAccount { Id = "account-a", Cookie = "session-a" };
        var room = CreateRoom(service, () => account);

        try
        {
            await room.ActivateDanmuConfigurationAsync();
            room.SelectedDanmuColor = room.DanmuColorOptions[1];
            var oldTask = room.DanmuConfigurationTask;
            service.Config = service.Config with { Current = new() { Mode = 5 } };

            if (changeRoom)
            {
                room.RoomId = "200";
            }
            else
            {
                account = new() { Id = "account-b", Cookie = "session-b" };
                room.AccountOverrideId = "account-b";
            }

            await room.DanmuConfigurationTask;
            Assert.True(oldToken.IsCancellationRequested);
            oldSave.SetException(new IOException("旧保存失败"));
            await oldTask;
            Assert.Equal(5, room.SelectedDanmuMode?.Value);
            Assert.Equal(0xFFFFFF, room.SelectedDanmuColor?.Value);
            Assert.DoesNotContain("旧保存失败", room.DanmuConfigurationStatus);
            Assert.Single(service.SetRequests);
        }
        finally
        {
            room.DeactivateDanmuConfiguration();
        }
    }

    /// <summary>
    /// 关闭页面取消读取并退订账号变化，重新打开时才刷新。
    /// </summary>
    [AvaloniaFact]
    public async Task DeactivationCancelsAndUnsubscribes()
    {
        var service = new FakeBilibiliDanmuConfigService();
        var response = new TaskCompletionSource<DanmuRoomConfig>();
        var token = CancellationToken.None;
        service.ReadAsync = (_, _, cancellationToken, _) =>
        {
            token = cancellationToken;
            return response.Task;
        };
        var account = new BilibiliAccount { Cookie = "session-a" };
        var room = CreateRoom(service, () => account);
        var task = room.ActivateDanmuConfigurationAsync();
        room.DeactivateDanmuConfiguration();
        account.Cookie = "session-b";
        response.SetResult(service.Config);
        await task;

        Assert.True(token.IsCancellationRequested);
        Assert.Single(service.GetRequests);
        Assert.Empty(room.DanmuColorOptions);
        Assert.False(room.CanSelectDanmuColor);
        service.ReadAsync = null;
        await room.ActivateDanmuConfigurationAsync();
        Assert.Equal("session-b", service.GetRequests[1].Cookie);
        room.DeactivateDanmuConfiguration();
    }

    /// <summary>
    /// 缺少账号或 Cookie 时禁用控件并且不查询。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAccountDisablesSelectors(bool hasAccount)
    {
        var service = new FakeBilibiliDanmuConfigService();
        var room = CreateRoom(service, () => hasAccount ? new() : null);
        await room.ActivateDanmuConfigurationAsync();
        Assert.Empty(service.GetRequests);
        Assert.False(room.CanSelectDanmuColor);
        Assert.False(room.CanSelectDanmuMode);
        room.DeactivateDanmuConfiguration();
    }

    /// <summary>
    /// 连续修改后立即发送，会等完整保存队列完成并使用最终颜色和位置。
    /// </summary>
    [AvaloniaFact]
    public async Task SendingWaitsForLatestConfigurationSave()
    {
        var service = new FakeBilibiliDanmuConfigService();
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        service.WriteAsync = async (_, _, options, _) =>
        {
            await (service.SetRequests.Count == 1 ? first.Task : second.Task);
            service.Config = service.Config with { Current = options };
            return service.Config;
        };
        using var handler = new DanmuSendHttpHandler { ResponseJson = "{\"code\":0,\"data\":{}}" };
        using var client = new HttpClient(handler);
        var sender = new DanmuSendService(client, service);
        var account = new BilibiliAccount { Cookie = "SESSDATA=test;bili_jct=test" };
        var room = CreateRoom(service, () => account, sender);

        try
        {
            await room.ActivateDanmuConfigurationAsync();
            room.SelectedDanmuColor = room.DanmuColorOptions[1];
            room.SelectedDanmuMode = room.DanmuModeOptions[2];
            room.InputDraft = "测试发送";
            var sending = room.SendDraftAsync();
            Assert.Equal(0, handler.RequestCount);
            first.SetResult();
            await Task.Yield();
            Assert.Equal(0, handler.RequestCount);
            second.SetResult();
            await room.DanmuConfigurationTask;
            await sending;
            var form = Assert.Single(handler.Forms);
            Assert.Equal("16711680", form["color"]);
            Assert.Equal("5", form["mode"]);
            Assert.Equal("", room.InputDraft);
        }
        finally
        {
            first.TrySetResult();
            second.TrySetResult();
            room.DeactivateDanmuConfiguration();
        }
    }

    /// <summary>
    /// 配置读取、保存和失败恢复均使用全局请求超时。
    /// </summary>
    [AvaloniaFact]
    public async Task ConfigurationRequestsUseGlobalTimeout()
    {
        var service = new FakeBilibiliDanmuConfigService
        {
            WriteAsync = (_, _, _, _) => Task.FromException<DanmuRoomConfig>(new IOException("保存失败"))
        };
        var account = new BilibiliAccount { Cookie = "session-a" };
        var room = CreateRoom(service, () => account, settings: new() { TimeoutSeconds = 6.5 });

        try
        {
            await room.ActivateDanmuConfigurationAsync();
            room.SelectedDanmuColor = room.DanmuColorOptions[1];
            await room.DanmuConfigurationTask;
            Assert.Equal(2, service.ReadTimeouts.Count);
            Assert.All(service.ReadTimeouts, timeout => Assert.Equal(TimeSpan.FromSeconds(6.5), timeout));
            Assert.Equal(TimeSpan.FromSeconds(6.5), Assert.Single(service.WriteTimeouts));
        }
        finally
        {
            room.DeactivateDanmuConfiguration();
        }
    }

    private static LiveRoomTabViewModel CreateRoom(FakeBilibiliDanmuConfigService service, Func<BilibiliAccount?> account,
        IDanmuSendService? sender = null, AppSettings? settings = null)
    {
        var room = new LiveRoomTabViewModel(new() { RoomId = "100" });
        room.Configure(_ => account(), (_, _) => account(), () => settings ?? new(), () => new(), () => [], _ => [],
            sender ?? new FakeDanmuSendService(), new FakeRoomAvatarService(), () => Task.CompletedTask,
            danmuConfigService: service);
        return room;
    }
}
