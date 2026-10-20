using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证服务端实际长度限制小于配置时仍能完整分段投递。</summary>
public class DanmuLengthRecoveryTests
{
    /// <summary>明确长度拒绝后刷新权限，重分段时不重复已接受的前缀。</summary>
    [Fact]
    public async Task LengthRejectionRefreshesConfigurationAndContinuesWithoutDuplicatingAcceptedContent()
    {
        using var handler = new DanmuSendHttpHandler();
        handler.ResponseBodies.Enqueue("{\"code\":0}");
        handler.ResponseBodies.Enqueue("{\"code\":10020,\"message\":\"超出限制长度\"}");
        handler.ResponseJson = "{\"code\":0}";
        using var client = new HttpClient(handler);
        var refreshes = 0;
        var config = new FakeBilibiliDanmuConfigService
        {
            ReadAsync = (_, _, _, refresh) =>
            {
                refreshes += refresh ? 1 : 0;
                return Task.FromResult(new Models.Workspace.DanmuRoomConfig { Length = refresh ? 10 : 20 });
            }
        };
        var sender = new DanmuSendService(client, config);
        var request = CreateRequest(new string('中', 50));
        var records = new List<Models.Workspace.DanmuFeedItem>();
        sender.RecordCreated += (_, record) => records.Add(record);

        var result = await sender.SendAsync(request, CreateAccount(), new() { SendIntervalMs = 0 });

        Assert.True(result.IsComplete);
        Assert.Equal(1, refreshes);
        Assert.All(result.Results, part => Assert.True(part.IsAccepted));
        Assert.Equal(result.Results.Count, records.Count);
        Assert.Equal(request.Contents[0].Body, Reassemble(result));
        Assert.Single(handler.Forms, form => form["msg"] == "【" + new string('中', 18) + "】");
        Assert.All(handler.Forms.Skip(2), form => Assert.InRange(form["msg"].Length, 3, 10));
    }

    /// <summary>配置刷新后仍不准确时，根据明确拒绝缩短分段，保留 Unicode 文字元素和符号。</summary>
    [Theory]
    [InlineData("这是需要完整发送的中文长句，这是后续文字", 20, 10)]
    [InlineData("甲👨‍👩‍👧‍👦乙👨‍👩‍👧‍👦丙", 40, 14)]
    [InlineData("甲乙丙丁戊己", 20, 3)]
    [InlineData("中中中中中中中中中中中中中中中中中中中中👨‍👩‍👧‍👦尾", 20, 14)]
    public async Task StaleLimitIsReducedUntilRejectedContentCanBeDelivered(string body, int configuredLimit, int actualLimit)
    {
        using var handler = new DanmuSendHttpHandler { MaximumMessageLength = actualLimit, ResponseJson = "{\"code\":0}" };
        using var client = new HttpClient(handler);
        var config = new FakeBilibiliDanmuConfigService { Config = new() { Length = configuredLimit } };
        var sender = new DanmuSendService(client, config);

        var result = await sender.SendAsync(CreateRequest(body), CreateAccount(), new() { SendIntervalMs = 0 });

        Assert.True(result.IsComplete);
        Assert.Equal(2, config.GetRequests.Count);
        Assert.True(handler.Forms.Count > 1);
        Assert.All(handler.Forms.Skip(1), form => Assert.True(form["msg"].Length < handler.Forms[0]["msg"].Length));
        Assert.Equal(body, Reassemble(result));
        Assert.All(result.Results, part => Assert.True(part.IsAccepted));
        Assert.All(result.Results, part => Assert.InRange(part.Record.Content.Length, 3, actualLimit));
        Assert.InRange(handler.RequestCount - result.Results.Count, 1, 8);
    }

    /// <summary>单个完整文字元素也被拒绝时有界停止，不能吞掉错误或循环重发。</summary>
    [Theory]
    [InlineData("中")]
    [InlineData("👨‍👩‍👧‍👦")]
    public async Task SmallestTextElementRejectionPreservesFailureAndStops(string body)
    {
        using var handler = new DanmuSendHttpHandler
        {
            ResponseJson = "{\"code\":10020,\"message\":\"超出限制长度\"}"
        };
        using var client = new HttpClient(handler);
        var sender = new DanmuSendService(client, new FakeBilibiliDanmuConfigService());

        var result = await sender.SendAsync(CreateRequest(body), CreateAccount(), new() { SendIntervalMs = 0 });

        Assert.False(result.IsComplete);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("超出限制长度", Assert.Single(result.Results).ErrorMessage);
        Assert.Equal(0, result.Progress.GetAcceptedLength(0));
    }

    /// <summary>重分段后再次遇到业务失败，续发沿用经验长度并跳过已接受的正文。</summary>
    [Fact]
    public async Task ContinuationKeepsRecoveredLengthAndAcceptedPrefix()
    {
        using var handler = new DanmuSendHttpHandler { ResponseJson = "{\"code\":0}" };
        handler.ResponseBodies.Enqueue("{\"code\":10020,\"message\":\"超出限制长度\"}");
        handler.ResponseBodies.Enqueue("{\"code\":0}");
        handler.ResponseBodies.Enqueue("{\"code\":0,\"message\":\"f\"}");
        using var client = new HttpClient(handler);
        var sender = new DanmuSendService(client, new FakeBilibiliDanmuConfigService());
        var request = CreateRequest(new string('中', 45));
        var account = CreateAccount();
        var settings = new AppSettings { SendIntervalMs = 0 };

        var first = await sender.SendAsync(request, account, settings);
        Assert.False(first.IsComplete);
        Assert.Equal(9, first.Progress.GetAcceptedLength(0));
        var sentBeforeContinuation = handler.RequestCount;
        var result = await sender.SendAsync(request, account, settings, first.Progress);

        Assert.True(result.IsComplete);
        Assert.Same(first.Progress, result.Progress);
        Assert.Equal(request.Contents[0].Body, Reassemble(result));
        Assert.Single(handler.Forms, form => form["msg"] == "【" + new string('中', 9) + "】");
        Assert.All(handler.Forms.Skip(sentBeforeContinuation), form => Assert.InRange(form["msg"].Length, 3, 11));
    }

    private static DanmuSendRequest CreateRequest(string body)
    {
        return new() { RoomId = "100", Contents = [new() { Body = body, Prefix = "【", Suffix = "】" }] };
    }

    private static BilibiliAccount CreateAccount()
    {
        return new() { Id = "offline", Cookie = "SESSDATA=test;bili_jct=test;DedeUserID=42" };
    }

    private static string Reassemble(DanmuDeliveryResult result)
    {
        return string.Concat(result.Results.Select((part, index) =>
            part.Record.Content[(index > 0 && part.Record.Content[1] == '…' ? 2 : 1)..^1]));
    }
}
