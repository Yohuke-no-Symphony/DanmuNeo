using System.Net;
using System.Threading;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 使用真实 API 序列化与离线 HTTP 响应验证音乐业务错误的传播。
/// </summary>
public class MusicLyricImportServiceTests
{
    /// <summary>
    /// 搜索在业务拒绝时保留服务端说明，不能映射成空候选。
    /// </summary>
    [Theory]
    [InlineData("wy", "{\"code\":401,\"message\":\"需要登录\"}", "网易云音乐搜索", "401", "需要登录")]
    [InlineData("qq", "{\"code\":1000,\"msg\":\"访问受限\"}", "QQ 音乐搜索", "1000", "访问受限")]
    [InlineData("wy", "{\"code\":0}", "网易云音乐搜索", "0", null)]
    [InlineData("qq", "{\"code\":200}", "QQ 音乐搜索", "200", null)]
    public async Task SearchRejectsBusinessErrors(string source, string json, string operation, string code,
        string? reason)
    {
        using var handler = new LiveRoomSearchHttpHandler { ResponseJson = json };
        using var client = new HttpClient(handler);
        var service = new MusicLyricImportService(client);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SearchAsync("歌曲", source, ""));

        Assert.Contains(operation, error.Message);
        Assert.Contains(code, error.Message);

        if (reason is not null)
        {
            Assert.Contains(reason, error.Message);
        }
    }

    /// <summary>
    /// 返回了部分歌词也不能覆盖接口的业务失败码。
    /// </summary>
    [Theory]
    [InlineData("wy", "{\"code\":403,\"message\":\"版权受限\",\"lrc\":{\"lyric\":\"错误歌词\"}}")]
    [InlineData("qq", "{\"code\":1000,\"msg\":\"版权受限\",\"lyric\":\"错误歌词\"}")]
    public async Task ImportRejectsBusinessErrorsBeforeUsingPartialLyrics(string source, string json)
    {
        using var handler = new LiveRoomSearchHttpHandler { ResponseJson = json };
        using var client = new HttpClient(handler);
        var service = new MusicLyricImportService(client);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(new()
        {
            Source = source,
            SourceSongId = "42",
            SongMid = "mid-42"
        }, ""));

        Assert.Contains("版权受限", error.Message);
        Assert.Contains("歌词获取", error.Message);
    }

    /// <summary>
    /// 成功响应中的空数组仍代表正常无结果。
    /// </summary>
    [Theory]
    [InlineData("wy", "{\"code\":200,\"result\":{\"songs\":[]}}")]
    [InlineData("qq", "{\"code\":0,\"data\":{\"song\":{\"list\":[]}}}")]
    public async Task SuccessfulEmptySearchRemainsEmpty(string source, string json)
    {
        using var handler = new LiveRoomSearchHttpHandler { ResponseJson = json };
        using var client = new HttpClient(handler);

        var results = await new MusicLyricImportService(client).SearchAsync("不存在的歌", source, "");

        Assert.Empty(results);
    }

    /// <summary>
    /// 缺失搜索结构的响应不会被误报为正常的零候选。
    /// </summary>
    [Theory]
    [InlineData("wy", "{\"code\":200}")]
    [InlineData("qq", "{\"code\":0,\"data\":{}}")]
    public async Task MissingSearchDataIsAnExplicitFailure(string source, string json)
    {
        using var handler = new LiveRoomSearchHttpHandler { ResponseJson = json };
        using var client = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new MusicLyricImportService(client).SearchAsync("歌曲", source, ""));

        Assert.Contains("未返回搜索结果", error.Message);
    }

    /// <summary>
    /// 网易云歌曲和歌词经过原 API 解析，使用外部客户端后仍可连续调用。
    /// </summary>
    [Fact]
    public async Task NetEaseSuccessPreservesMetadataAndClientOwnership()
    {
        using var handler = new LiveRoomSearchHttpHandler
        {
            ResponseJson = """
                           {"code":200,"result":{"songs":[{"id":42,"name":"歌曲","artists":[{"name":"歌手"}],"album":{"name":"专辑"}}]}}
                           """
        };
        using var client = new HttpClient(handler);
        var service = new MusicLyricImportService(client);

        var result = Assert.Single(await service.SearchAsync(" 歌曲 ", "wy", ""));
        Assert.Equal("42", result.SourceSongId);
        Assert.Equal("歌曲", result.Title);
        Assert.Equal("歌手", result.Artist);
        Assert.Equal("专辑", result.Album);
        Assert.Contains("s=", handler.RequestUri!.Query);
        handler.ResponseJson = """{"code":200,"lrc":{"lyric":"[00:01]原文"},"tlyric":{"lyric":"[00:01]译文"}}""";

        var item = await service.ImportAsync(result, "");

        Assert.Equal("[00:01]原文", item.LyricText);
        Assert.Equal("[00:01]译文", item.TranslatedLyricText);
        Assert.Equal("歌曲", item.Title);
        Assert.True(item.HasTranslation);
        Assert.Contains("id=42", handler.RequestUri!.Query);
    }

    /// <summary>
    /// QQ 音乐保留歌曲 MID，并在 API 释放后继续复用外部 HTTP 客户端。
    /// </summary>
    [Fact]
    public async Task QQMusicSuccessPreservesMetadataAndClientOwnership()
    {
        using var handler = new LiveRoomSearchHttpHandler
        {
            ResponseJson = """
                           {"code":0,"data":{"song":{"list":[{"songid":42,"songmid":"mid-42","songname":"歌曲","singer":[{"name":"歌手"}],"albumname":"专辑"}]}}}
                           """
        };
        using var client = new HttpClient(handler);
        var service = new MusicLyricImportService(client);

        var result = Assert.Single(await service.SearchAsync("歌曲", "qq", ""));
        Assert.Equal("42", result.SourceSongId);
        Assert.Equal("mid-42", result.SongMid);
        Assert.Equal("歌手", result.Artist);
        Assert.Equal("专辑", result.Album);
        handler.ResponseJson = """{"code":0,"lyric":"[00:01]原文","trans":"[00:01]译文"}""";

        var item = await service.ImportAsync(result, "");

        Assert.Equal("[00:01]原文", item.LyricText);
        Assert.Equal("[00:01]译文", item.TranslatedLyricText);
        Assert.True(item.HasTranslation);
        Assert.Contains("songmid=mid-42", handler.RequestUri!.Query);
    }

    /// <summary>
    /// 空的歌词内容应产生明确失败，而不是导入一个空条目。
    /// </summary>
    [Theory]
    [InlineData("wy", "{\"code\":200,\"lrc\":{\"lyric\":\" \"}}")]
    [InlineData("qq", "{\"code\":0,\"lyric\":\"\"}")]
    public async Task ImportRejectsMissingLyrics(string source, string json)
    {
        using var handler = new LiveRoomSearchHttpHandler { ResponseJson = json };
        using var client = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new MusicLyricImportService(client)
            .ImportAsync(new() { Source = source, SourceSongId = "42", SongMid = "mid-42" }, ""));

        Assert.Contains("未返回可用的歌词内容", error.Message);
    }

    /// <summary>
    /// 传输失败继续保留 HTTP 异常，不被解释为正常业务结果。
    /// </summary>
    [Theory]
    [InlineData("wy")]
    [InlineData("qq")]
    public async Task TransportFailureIsNotConvertedIntoEmptyResults(string source)
    {
        using var handler = new DanmuSendHttpHandler
        {
            StatusCode = HttpStatusCode.ServiceUnavailable,
            ResponseJson = "unavailable"
        };
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new MusicLyricImportService(client).SearchAsync("歌曲", source, ""));
    }

    /// <summary>
    /// 调用方取消保留取消异常，使上层能区别正常取消与业务失败。
    /// </summary>
    [Theory]
    [InlineData("wy", false)]
    [InlineData("qq", false)]
    [InlineData("wy", true)]
    [InlineData("qq", true)]
    public async Task CallerCancellationIsPreserved(string source, bool import)
    {
        using var handler = new LiveRoomSearchHttpHandler();
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var service = new MusicLyricImportService(client);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (import)
            {
                await service.ImportAsync(new() { Source = source, SourceSongId = "42", SongMid = "mid-42" }, "",
                    cancellation.Token);
            }
            else
            {
                await service.SearchAsync("歌曲", source, "", cancellation.Token);
            }
        });
        Assert.Null(handler.RequestUri);
    }
}
