using Newtonsoft.Json.Linq;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>发送策略与兼容行为的离线回归测试。</summary>
public class DanmuLengthConfigurationTests
{
    /// <summary>当前账号房间上限优先，缺失时才回退配置上限。</summary>
    [Theory]
    [InlineData(40, 20, 40)]
    [InlineData(0, 30, 30)]
    [InlineData(-1, 0, 0)]
    public void AccountRoomLimitWinsThenFallsBackToConfig(int userLength, int configLength, int expected)
    {
        var result = BilibiliDanmuConfigSelector.Select(new BilibiliDanmuConfigData
        {
            Length = configLength,
            Group = JArray.Parse("[]"),
            Mode = JArray.Parse("[]")
        }, new BilibiliUserInfoData
        {
            Property = JObject.FromObject(new { danmu = new { color = 16777215, mode = 1, length = userLength } })
        });
        Assert.Equal(expected, result.Length);
    }

    /// <summary>确认配置在六十秒后重新读取服务端能力。</summary>
    [Fact]
    public async Task ConfirmedConfigurationExpiresAfterSixtySeconds()
    {
        using var handler = new DanmuConfigHttpHandler();
        foreach (var length in new[] { 20, 40 })
        {
            handler.Responses.Enqueue("{\"code\":0,\"data\":{\"group\":[],\"mode\":[]}}");
            handler.Responses.Enqueue("{\"code\":0,\"data\":{\"property\":{\"danmu\":{\"color\":16777215,\"mode\":1,\"length\":" + length + "}}}}");
        }
        using var client = new HttpClient(handler);
        var clock = new ManualSendTimeProvider();
        var service = new BilibiliDanmuConfigService(client, clock);
        var first = await service.GetConfigAsync("100", "SESSDATA=test");
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Same(first, await service.GetConfigAsync("100", "SESSDATA=test"));
        clock.Advance(TimeSpan.FromSeconds(1));
        var next = await service.GetConfigAsync("100", "SESSDATA=test");
        Assert.Equal(20, first.Length);
        Assert.Equal(40, next.Length);
        Assert.Equal(4, handler.Requests.Count);
    }
}
