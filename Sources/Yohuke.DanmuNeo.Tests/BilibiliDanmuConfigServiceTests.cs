using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证弹幕可用选项、服务端保存和账号身份隔离。
/// </summary>
public class BilibiliDanmuConfigServiceTests
{
    private const string COOKIE = "SESSDATA=test;bili_jct=csrf-test;DedeUserID=42";
    private const string CONFIG_JSON = """
        {"code":0,"data":{"color":[{"color_hex":"FF0000","name":"错误来源","status":1}],
        "group":[{"color":[
            {"color_hex":"FFFFFF","name":"白色","status":1},
            {"color_hex":"0000ff","name":"蓝色","status":1},
            {"color_hex":"FF0000","name":"未解锁","status":0}]},
            {"color":[{"color_hex":"0x00fFfF","name":"青色","status":true},
            {"color_hex":"ffffff","name":"重复","status":1},
            {"color_hex":"invalid","name":"损坏","status":1},
            {"color_hex":"1000000","name":"超出范围","status":1}]}],
        "mode":[{"mode":1,"name":"滚动","status":1},
            {"mode":4,"name":"底部","status":0},
            {"mode":5,"name":"顶部","status":true},
            {"mode":5,"name":"重复","status":1}]}}
        """;

    /// <summary>
    /// 按旧项目协议展开分组颜色，过滤未解锁项，并读取当前选项。
    /// </summary>
    [Fact]
    public async Task ReadsAvailableOptionsAndCurrentSelectionWithAccountCookie()
    {
        using var handler = new DanmuConfigHttpHandler();
        EnqueueConfig(handler, 255, 5);
        using var client = new HttpClient(handler);
        var service = new BilibiliDanmuConfigService(client);

        var config = await service.GetConfigAsync("100", COOKIE);

        Assert.Equal(new[] { 0xFFFFFF, 255, 65535 }, config.Colors.Select(option => option.Value));
        Assert.Equal(new[] { "白色", "蓝色", "青色" }, config.Colors.Select(option => option.Name));
        Assert.Equal("#0000FF", config.Colors[1].ColorHex);
        Assert.Equal(new[] { 1, 5 }, config.Modes.Select(option => option.Value));
        Assert.Equal(new DanmuSendOptions { Color = 255, Mode = 5 }, config.Current);
        Assert.Equal(new[]
        {
            "/xlive/web-room/v1/dM/GetDMConfigByGroup",
            "/xlive/web-room/v1/index/getInfoByUser"
        }, handler.Requests.Select(uri => uri.AbsolutePath));
        Assert.All(handler.Requests, uri => Assert.Contains("room_id=100", uri.Query));
        Assert.All(handler.Cookies, cookie => Assert.Contains("SESSDATA=test", cookie));
    }

    /// <summary>
    /// 损坏的可选颜色项不会使其他合法选项无法加载。
    /// </summary>
    [Theory]
    [InlineData("{\"color_hex\":{},\"name\":\"损坏颜色\",\"status\":1}")]
    [InlineData("{\"color_hex\":[],\"name\":\"损坏颜色\",\"status\":1}")]
    public async Task MalformedOptionalColorDoesNotHideValidOptions(string item)
    {
        using var handler = new DanmuConfigHttpHandler();
        var response = JObject.Parse(CONFIG_JSON);
        var colors = Assert.IsType<JArray>(response["data"]!["group"]![0]!["color"]);
        colors.Add(JObject.Parse(item));
        handler.Responses.Enqueue(response.ToString());
        handler.Responses.Enqueue(CreateUserJson(255, 5));
        using var client = new HttpClient(handler);
        var service = new BilibiliDanmuConfigService(client);

        var config = await service.GetConfigAsync("100", COOKIE);

        Assert.Equal(new[] { 0xFFFFFF, 255, 65535 }, config.Colors.Select(option => option.Value));
    }

    /// <summary>
    /// 同一身份复用已确认配置，切换房间或 Cookie 后重新读取。
    /// </summary>
    [Theory]
    [InlineData("101", COOKIE)]
    [InlineData("100", "SESSDATA=other;bili_jct=csrf-test;DedeUserID=43")]
    [InlineData("100", "SESSDATA=renewed;bili_jct=csrf-test;DedeUserID=42")]
    public async Task CacheSeparatesRoomsAndCookieIdentities(string nextRoomId, string nextCookie)
    {
        using var handler = new DanmuConfigHttpHandler();
        EnqueueConfig(handler, 255, 5);
        EnqueueConfig(handler, 65535, 1);
        using var client = new HttpClient(handler);
        var service = new BilibiliDanmuConfigService(client);

        var first = await service.GetConfigAsync("100", COOKIE);
        Assert.Same(first, await service.GetConfigAsync("00100", COOKIE));
        var next = await service.GetConfigAsync(nextRoomId, nextCookie);

        Assert.Equal(new DanmuSendOptions { Color = 65535, Mode = 1 }, next.Current);
        Assert.Same(first, await service.GetConfigAsync("100", COOKIE));
        Assert.Equal(4, handler.Requests.Count);
    }

    /// <summary>
    /// 主动刷新可以读取在其他客户端修改后的服务端配置。
    /// </summary>
    [Fact]
    public async Task ForceRefreshReplacesConfirmedConfiguration()
    {
        using var handler = new DanmuConfigHttpHandler();
        EnqueueConfig(handler, 255, 5);
        EnqueueConfig(handler, 65535, 1);
        using var client = new HttpClient(handler);
        var service = new BilibiliDanmuConfigService(client);
        await service.GetConfigAsync("100", COOKIE);

        var refreshed = await service.GetConfigAsync("100", COOKIE, forceRefresh: true);

        Assert.Equal(new DanmuSendOptions { Color = 65535, Mode = 1 }, refreshed.Current);
        Assert.Same(refreshed, await service.GetConfigAsync("100", COOKIE));
        Assert.Equal(4, handler.Requests.Count);
    }

    /// <summary>
    /// 保存使用十六进制颜色和两个 CSRF 字段，只有成功后才替换已确认选项。
    /// </summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task SavesSelectionAndAcceptsOperationResponseShapes(string data)
    {
        using var handler = new DanmuConfigHttpHandler();
        EnqueueConfig(handler, 255, 5);
        handler.Responses.Enqueue($$"""{"code":0,"data":{{data}}}""");
        using var client = new HttpClient(handler);
        var service = new BilibiliDanmuConfigService(client);
        var options = new DanmuSendOptions { Color = 65535, Mode = 1 };

        var saved = await service.SetConfigAsync("100", COOKIE, options);

        Assert.Equal(options, saved.Current);
        Assert.Same(saved, await service.GetConfigAsync("100", COOKIE));
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("/xlive/web-room/v1/dM/AjaxSetConfig", handler.Requests[2].AbsolutePath);
        Assert.Equal("100", handler.Forms[2]["room_id"]);
        Assert.Equal("0x00FFFF", handler.Forms[2]["color"]);
        Assert.Equal("1", handler.Forms[2]["mode"]);
        Assert.Equal("csrf-test", handler.Forms[2]["csrf"]);
        Assert.Equal("csrf-test", handler.Forms[2]["csrf_token"]);
        Assert.Contains("SESSDATA=test", handler.Cookies[2]);
    }

    /// <summary>
    /// 保存被接口拒绝时仍保留此前成功读取的选项。
    /// </summary>
    [Fact]
    public async Task RejectedSavePreservesConfirmedSelection()
    {
        using var handler = new DanmuConfigHttpHandler();
        EnqueueConfig(handler, 255, 5);
        handler.Responses.Enqueue("""{"code":-403,"message":"","msg":"颜色不可用"}""");
        using var client = new HttpClient(handler);
        var service = new BilibiliDanmuConfigService(client);
        var confirmed = await service.GetConfigAsync("100", COOKIE);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetConfigAsync("100", COOKIE, new() { Color = 65535, Mode = 1 }));

        Assert.Equal("颜色不可用", error.Message);
        Assert.Same(confirmed, await service.GetConfigAsync("100", COOKIE));
        Assert.Equal(3, handler.Requests.Count);
    }

    /// <summary>
    /// 未解锁或格式无效的选择不会提交保存请求。
    /// </summary>
    [Theory]
    [InlineData(0xFF0000, 1)]
    [InlineData(255, 4)]
    [InlineData(-1, 1)]
    [InlineData(0x1000000, 1)]
    [InlineData(255, 0)]
    public async Task InvalidOrUnavailableSelectionIsNotPosted(int color, int mode)
    {
        using var handler = new DanmuConfigHttpHandler();
        EnqueueConfig(handler, 255, 5);
        using var client = new HttpClient(handler);
        var service = new BilibiliDanmuConfigService(client);
        await service.GetConfigAsync("100", COOKIE);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SetConfigAsync("100", COOKIE, new() { Color = color, Mode = mode }));

        Assert.Equal(2, handler.Requests.Count);
    }

    /// <summary>
    /// 查询失败或当前选项不完整时不缓存伪造的默认配置。
    /// </summary>
    [Theory]
    [InlineData("config", "{\"code\":-101,\"msg\":\"未登录\"}")]
    [InlineData("config", "{\"code\":0,\"data\":null}")]
    [InlineData("user", "{\"code\":-352,\"message\":\"-352\"}")]
    [InlineData("user", "{\"code\":0,\"data\":{}}")]
    [InlineData("user", "{\"code\":0,\"data\":{\"property\":{\"danmu\":{\"color\":-1,\"mode\":1}}}}")]
    [InlineData("user", "{\"code\":0,\"data\":{\"property\":{\"danmu\":{\"color\":255,\"mode\":0}}}}")]
    public async Task ReadFailureDoesNotPopulateCache(string stage, string response)
    {
        using var handler = new DanmuConfigHttpHandler();
        handler.Responses.Enqueue(stage == "config" ? response : CONFIG_JSON);

        if (stage == "user" || response.Contains("\"code\":0"))
        {
            handler.Responses.Enqueue(stage == "user" ? response : CreateUserJson(255, 5));
        }

        EnqueueConfig(handler, 65535, 1);
        using var client = new HttpClient(handler);
        var service = new BilibiliDanmuConfigService(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetConfigAsync("100", COOKIE));
        var retried = await service.GetConfigAsync("100", COOKIE);

        Assert.Equal(new DanmuSendOptions { Color = 65535, Mode = 1 }, retried.Current);
    }

    /// <summary>
    /// 连续保存按调用顺序写入，最终缓存与最后一次成功保存一致。
    /// </summary>
    [Fact]
    public async Task ConcurrentSavesAreSerialized()
    {
        using var handler = new DanmuConfigHttpHandler();
        EnqueueConfig(handler, 0xFFFFFF, 1);
        handler.Responses.Enqueue("""{"code":0,"data":{}}""");
        handler.Responses.Enqueue("""{"code":0,"data":{}}""");
        using var client = new HttpClient(handler);
        var service = new BilibiliDanmuConfigService(client);
        await service.GetConfigAsync("100", COOKIE);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        handler.BeforeResponseAsync = async (request, token) =>
        {
            if (request.Method == HttpMethod.Post && Interlocked.Increment(ref writes) == 1)
            {
                started.SetResult();
                await release.Task.WaitAsync(token);
            }
        };

        var first = service.SetConfigAsync("100", COOKIE, new() { Color = 255, Mode = 1 });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = service.SetConfigAsync("100", COOKIE, new() { Color = 65535, Mode = 5 });
        Assert.Equal(3, handler.Requests.Count);
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(new DanmuSendOptions { Color = 65535, Mode = 5 },
            (await service.GetConfigAsync("100", COOKIE)).Current);
        Assert.Equal(new[] { "0x0000FF", "0x00FFFF" }, handler.Forms.Skip(2).Select(form => form["color"]));
    }

    /// <summary>
    /// 保存发出后响应被取消时重新读取，避免使用服务端可能已修改的旧值。
    /// </summary>
    [Fact]
    public async Task CancelledSaveInvalidatesUncertainCache()
    {
        using var handler = new DanmuConfigHttpHandler();
        EnqueueConfig(handler, 255, 5);
        using var client = new HttpClient(handler);
        var service = new BilibiliDanmuConfigService(client);
        var confirmed = await service.GetConfigAsync("100", COOKIE);
        EnqueueConfig(handler, 65535, 1);
        using var cancellation = new CancellationTokenSource();
        handler.BeforeResponseAsync = (request, token) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }

            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SetConfigAsync("100", COOKIE,
            new() { Color = 65535, Mode = 1 }, cancellationToken: cancellation.Token));

        var refreshed = await service.GetConfigAsync("100", COOKIE);
        Assert.NotSame(confirmed, refreshed);
        Assert.Equal(new DanmuSendOptions { Color = 65535, Mode = 1 }, refreshed.Current);
        Assert.Equal(5, handler.Requests.Count);
    }

    /// <summary>
    /// 保存响应超时、断开或损坏时，下次读取重新确认服务端值。
    /// </summary>
    [Theory]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("json")]
    public async Task UncertainSaveResponseInvalidatesCache(string mode)
    {
        using var handler = new DanmuConfigHttpHandler();
        EnqueueConfig(handler, 255, 5);
        using var client = new HttpClient(handler);
        var service = new BilibiliDanmuConfigService(client);
        await service.GetConfigAsync("100", COOKIE);

        if (mode == "json")
        {
            handler.Responses.Enqueue("损坏的响应");
        }

        EnqueueConfig(handler, 65535, 1);
        handler.BeforeResponseAsync = (request, _) =>
        {
            if (request.Method == HttpMethod.Post && mode != "json")
            {
                return Task.FromException(mode == "network"
                    ? new HttpRequestException("响应连接中断")
                    : new TaskCanceledException("响应超时"));
            }

            return Task.CompletedTask;
        };

        var error = await Record.ExceptionAsync(() => service.SetConfigAsync("100", COOKIE,
            new() { Color = 65535, Mode = 1 }));
        Assert.NotNull(error);
        var refreshed = await service.GetConfigAsync("100", COOKIE);

        Assert.Equal(new DanmuSendOptions { Color = 65535, Mode = 1 }, refreshed.Current);
        Assert.Equal(5, handler.Requests.Count);
    }

    /// <summary>
    /// 读取取消后释放串行锁，并允许下次请求重新加载。
    /// </summary>
    [Fact]
    public async Task CancelledReadCanBeRetried()
    {
        using var handler = new DanmuConfigHttpHandler();
        EnqueueConfig(handler, 255, 5);
        using var client = new HttpClient(handler);
        var service = new BilibiliDanmuConfigService(client);
        using var cancellation = new CancellationTokenSource();
        var cancelled = false;
        handler.BeforeResponseAsync = (_, token) =>
        {
            if (!cancelled)
            {
                cancelled = true;
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }

            return Task.CompletedTask;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetConfigAsync("100", COOKIE, cancellationToken: cancellation.Token));
        var retried = await service.GetConfigAsync("100", COOKIE);

        Assert.Equal(new DanmuSendOptions { Color = 255, Mode = 5 }, retried.Current);
        Assert.Equal(3, handler.Requests.Count);
    }

    /// <summary>
    /// 成功保存的配置进入实际发送表单，读取缓存不产生额外查询。
    /// </summary>
    [Fact]
    public async Task SavedSelectionFlowsIntoActualSendRequest()
    {
        using var handler = new DanmuConfigHttpHandler();
        EnqueueConfig(handler, 255, 5);
        handler.Responses.Enqueue("""{"code":0,"data":[]}""");
        handler.Responses.Enqueue("""{"code":0,"data":{}}""");
        using var client = new HttpClient(handler);
        var configService = new BilibiliDanmuConfigService(client);
        await configService.SetConfigAsync("100", COOKIE, new() { Color = 65535, Mode = 1 });
        var sender = new DanmuSendService(client, configService);

        var result = await sender.SendAsync("100", "同传", new() { Cookie = COOKIE }, new() { SendIntervalMs = 0 });

        Assert.True(result.IsAccepted);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("/msg/send", handler.Requests[3].AbsolutePath);
        Assert.Equal("65535", handler.Forms[3]["color"]);
        Assert.Equal("1", handler.Forms[3]["mode"]);
        Assert.Equal("同传", handler.Forms[3]["msg"]);
    }

    private static void EnqueueConfig(DanmuConfigHttpHandler handler, int color, int mode)
    {
        handler.Responses.Enqueue(CONFIG_JSON);
        handler.Responses.Enqueue(CreateUserJson(color, mode));
    }

    private static string CreateUserJson(int color, int mode)
    {
        return JsonConvert.SerializeObject(new
        {
            code = 0,
            data = new { property = new { danmu = new { color, mode, length = 20 } } }
        });
    }
}
