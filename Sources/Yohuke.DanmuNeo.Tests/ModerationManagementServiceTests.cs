using System.Net;
using System.Threading;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证禁言名单分页、解除和任意用户查找的离线契约。</summary>
public class ModerationManagementServiceTests
{
    /// <summary>名单按 POST 页码遍历，保留目标、执行人和原始时间，去重重复记录。</summary>
    [Fact]
    public async Task ListUsesPostPageNumbersAndMapsActualFields()
    {
        using var handler = new ModerationHttpHandler();
        handler.Responses.Enqueue("""{"code":0,"data":{"data":[{"id":9,"tuid":30,"tname":"目标","ctime":"2026-10-19 10:00:00","name":"执行人"}],"total_page":2}}""");
        handler.Responses.Enqueue("""{"code":0,"data":{"data":[{"id":9,"tuid":30,"tname":"目标"},{"id":10,"tuid":31,"tname":"另一人"}],"total_page":2}}""");
        using var client = new HttpClient(handler);
        var service = new BilibiliModerationService(client);

        var users = await service.GetMutedUsersAsync(100, "SESSDATA=offline;bili_jct=csrf");

        Assert.Equal(2, users.Count);
        Assert.Equal(new[] { HttpMethod.Post, HttpMethod.Post }, handler.Methods);
        var forms = handler.Forms.Select(ParseForm).ToArray();
        Assert.Equal(new[] { "1", "2" }, forms.Select(form => form["ps"]));
        Assert.All(forms, form =>
        {
            Assert.Equal("100", form["room_id"]);
            Assert.Equal("csrf", form["csrf"]);
            Assert.Equal("csrf", form["csrf_token"]);
        });
        var user = users[0];
        Assert.Equal(9, user.RecordId);
        Assert.Equal(30, user.UserUid);
        Assert.Equal(100, user.RoomId);
        Assert.Equal("目标", user.UserName);
        Assert.Equal("执行人", user.ExecutorName);
        Assert.Equal("2026-10-19 10:00:00", user.CreatedAtText);
    }

    /// <summary>缺少必要结构不能静默成为空名单。</summary>
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"data\":[null],\"total_page\":1}")]
    [InlineData("{\"list\":[],\"count\":0}")]
    [InlineData("{\"data\":[]}")]
    [InlineData("{\"total_page\":1}")]
    [InlineData("{\"data\":[{\"tuid\":30}],\"total_page\":1}")]
    public async Task ListRejectsMissingRequiredFields(string data)
    {
        using var handler = new ModerationHttpHandler();
        handler.Responses.Enqueue("{\"code\":0,\"data\":" + data + "}");
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new BilibiliModerationService(client).GetMutedUsersAsync(100, "SESSDATA=offline;bili_jct=csrf"));
    }

    /// <summary>服务器明确返回空名单仍是正常结果。</summary>
    [Fact]
    public async Task ListAcceptsExplicitEmptyPage()
    {
        using var handler = new ModerationHttpHandler();
        handler.Responses.Enqueue("""{"code":0,"data":{"data":[],"total_page":0}}""");
        using var client = new HttpClient(handler);

        Assert.Empty(await new BilibiliModerationService(client).GetMutedUsersAsync(100, "SESSDATA=offline;bili_jct=csrf"));
    }

    /// <summary>解除使用记录 ID，兼容空数组响应，并只在成功后通知其他消费者。</summary>
    [Fact]
    public async Task UnmuteUsesRecordIdAndPublishesRoomChange()
    {
        using var handler = new ModerationHttpHandler();
        handler.Responses.Enqueue("""{"code":0,"data":[]}""");
        using var client = new HttpClient(handler);
        var service = new BilibiliModerationService(client);
        var changed = new List<long>();
        service.Changed += (_, roomId) => changed.Add(roomId);

        await service.UnmuteUserAsync(100, 9, "SESSDATA=offline;bili_jct=csrf");

        Assert.Equal("100", handler.FormParameters["roomid"]);
        Assert.Equal("9", handler.FormParameters["id"]);
        Assert.Equal(new[] { 100L }, changed);
    }

    /// <summary>禁言成功也通知管理面板，使原有右键入口可以刷新名单。</summary>
    [Fact]
    public async Task ExistingMutePublishesRoomChange()
    {
        using var handler = new ModerationHttpHandler();
        handler.Responses.Enqueue("""{"code":0,"data":{}}""");
        using var client = new HttpClient(handler);
        var service = new BilibiliModerationService(client);
        var changed = new List<long>();
        service.Changed += (_, roomId) => changed.Add(roomId);

        await service.MuteUserAsync(100, 30, "SESSDATA=offline;bili_jct=csrf", "原始内容");

        Assert.Equal(new[] { 100L }, changed);
    }

    /// <summary>明确未登录保留专用异常，失败操作不通知变更。</summary>
    [Fact]
    public async Task ExpiredUnmuteDoesNotPublishChange()
    {
        using var handler = new ModerationHttpHandler();
        handler.Responses.Enqueue("""{"code":-101,"message":"账号未登录"}""");
        using var client = new HttpClient(handler);
        var service = new BilibiliModerationService(client);
        var changed = false;
        service.Changed += (_, _) => changed = true;

        await Assert.ThrowsAsync<BilibiliAuthenticationException>(() =>
            service.UnmuteUserAsync(100, 9, "SESSDATA=offline;bili_jct=csrf"));
        Assert.False(changed);
    }

    /// <summary>昵称搜索查询所有 B 站用户，并保留分页，不能只搜主播。</summary>
    [Fact]
    public async Task NicknameSearchUsesBiliUserTypeAndPaging()
    {
        using var handler = new ModerationHttpHandler();
        handler.Responses.Enqueue("""{"code":0,"data":{"numPages":3,"result":[{"mid":30,"uname":"<em class=\"keyword\">目标</em>&amp;人"}]}}""");
        using var client = new HttpClient(handler);

        var page = await new BilibiliModerationService(client).SearchUsersAsync("  目标  ", "SESSDATA=offline", 2);

        var request = handler.Requests.Single();
        Assert.Contains("search_type=bili_user", request.Query);
        Assert.Contains("page=2", request.Query);
        Assert.Equal(30, Assert.Single(page.Users).Uid);
        Assert.Equal("目标&人", page.Users[0].UserName);
        Assert.True(page.HasMore);
    }

    /// <summary>纯数字通过公开名片精确查 UID，不需要其拥有直播间。</summary>
    [Fact]
    public async Task NumericSearchUsesExactPublicUserCard()
    {
        using var handler = new ModerationHttpHandler();
        handler.Responses.Enqueue("""{"code":0,"data":{"card":{"mid":30,"name":"普通用户"}}}""");
        using var client = new HttpClient(handler);

        var page = await new BilibiliModerationService(client).SearchUsersAsync("00030");

        Assert.EndsWith("/x/web-interface/card", handler.Requests.Single().AbsolutePath);
        Assert.Equal(30, Assert.Single(page.Users).Uid);
        Assert.False(page.HasMore);
    }

    private static IReadOnlyDictionary<string, string> ParseForm(string form)
    {
        return form.Split('&').Select(part => part.Split('=', 2))
            .ToDictionary(parts => WebUtility.UrlDecode(parts[0]), parts => WebUtility.UrlDecode(parts[1]));
    }
}
