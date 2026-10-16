using System.Net;
using System.Text.Json;
using System.Threading;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证 GitHub 正式发布筛选、版本比较和 HTTP 边界。
/// </summary>
public class GitHubAppUpdateServiceTests
{
    private const string RELEASE_PREFIX = "https://github.com/Yohuke-no-Symphony/DanmuNeo/releases/tag/";

    /// <summary>
    /// 以数字比较二至四段版本，并接受大小写 v 前缀。
    /// </summary>
    [Theory]
    [InlineData("0.0.3", "0.0.2")]
    [InlineData("v0.0.10", "0.0.9")]
    [InlineData("1.2.3.1", "1.2.3")]
    [InlineData("V2.0", "1.5.0")]
    public async Task HigherReleaseReturnsItsOwnPage(string tagName, string currentVersion)
    {
        using var handler = new AppUpdateHttpHandler { ResponseJson = CreateReleaseJson(tagName) };
        using var client = new HttpClient(handler);
        using var service = new GitHubAppUpdateService(client);

        var update = await service.CheckForUpdateAsync(Version.Parse(currentVersion));

        Assert.NotNull(update);
        Assert.Equal(tagName, update.TagName);
        Assert.Equal(RELEASE_PREFIX + Uri.EscapeDataString(tagName), update.ReleaseUri.AbsoluteUri);
        Assert.True(update.Version.Revision >= 0);
    }

    /// <summary>
    /// 相同版本的缺省段视为零，旧版和相同版本均不提示。
    /// </summary>
    [Theory]
    [InlineData("0.0.2", "0.0.2.0")]
    [InlineData("v0.0.2.0", "0.0.2")]
    [InlineData("1.2", "1.2.0.0")]
    [InlineData("0.0.9", "0.0.10")]
    [InlineData("1.0.0", "2.0.0")]
    public async Task EqualOrOlderReleaseIsIgnored(string tagName, string currentVersion)
    {
        using var handler = new AppUpdateHttpHandler { ResponseJson = CreateReleaseJson(tagName) };
        using var client = new HttpClient(handler);
        using var service = new GitHubAppUpdateService(client);

        Assert.Null(await service.CheckForUpdateAsync(Version.Parse(currentVersion)));
    }

    /// <summary>
    /// 草稿和预发布均不作为正式更新。
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task NonFinalReleaseIsIgnored(bool draft, bool prerelease)
    {
        using var handler = new AppUpdateHttpHandler
            { ResponseJson = CreateReleaseJson("2.0.0", draft, prerelease) };
        using var client = new HttpClient(handler);
        using var service = new GitHubAppUpdateService(client);

        Assert.Null(await service.CheckForUpdateAsync(new(1, 0, 0)));
    }

    /// <summary>
    /// 缺少版本、无效标签及不属于本仓库的发布链接不能打开更新提示。
    /// </summary>
    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"tag_name\":\"latest\"}")]
    [InlineData("{\"tag_name\":\"2.0.0-beta\"}")]
    [InlineData("{\"tag_name\":\"2.0.0\",\"html_url\":\"file:///tmp/update\"}")]
    [InlineData("{\"tag_name\":\"2.0.0\",\"html_url\":\"https://github.com/another/repo/releases/tag/2.0.0\"}")]
    public async Task UnusableReleaseIsIgnored(string responseJson)
    {
        using var handler = new AppUpdateHttpHandler { ResponseJson = responseJson };
        using var client = new HttpClient(handler);
        using var service = new GitHubAppUpdateService(client);

        Assert.Null(await service.CheckForUpdateAsync(new(1, 0, 0)));
    }

    /// <summary>
    /// 仓库尚无正式发布时不提示更新。
    /// </summary>
    [Fact]
    public async Task MissingReleaseReturnsNoUpdate()
    {
        using var handler = new AppUpdateHttpHandler { StatusCode = HttpStatusCode.NotFound };
        using var client = new HttpClient(handler);
        using var service = new GitHubAppUpdateService(client);

        Assert.Null(await service.CheckForUpdateAsync(new(1, 0, 0)));
    }

    /// <summary>
    /// 权限、限流和服务器错误由调用方处理，不能误报新版本。
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task HttpErrorsRemainFailures(HttpStatusCode statusCode)
    {
        using var handler = new AppUpdateHttpHandler { StatusCode = statusCode };
        using var client = new HttpClient(handler);
        using var service = new GitHubAppUpdateService(client);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.CheckForUpdateAsync(new(1, 0, 0)));
    }

    /// <summary>
    /// 损坏 JSON 不能生成更新结果。
    /// </summary>
    [Fact]
    public async Task InvalidJsonRemainsFailure()
    {
        using var handler = new AppUpdateHttpHandler { ResponseJson = "{ broken" };
        using var client = new HttpClient(handler);
        using var service = new GitHubAppUpdateService(client);

        await Assert.ThrowsAsync<JsonException>(() => service.CheckForUpdateAsync(new(1, 0, 0)));
    }

    /// <summary>
    /// 取消信号会传递到尚未完成的 HTTP 请求。
    /// </summary>
    [Fact]
    public async Task CancellationStopsPendingRequest()
    {
        using var handler = new AppUpdateHttpHandler { WaitForCancellation = true };
        using var client = new HttpClient(handler);
        using var service = new GitHubAppUpdateService(client);
        using var tokenSource = new CancellationTokenSource();
        var check = service.CheckForUpdateAsync(new(1, 0, 0), tokenSource.Token);

        tokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
    }

    /// <summary>
    /// GitHub 请求携带所需公开 API 头，不携带账号凭据。
    /// </summary>
    [Fact]
    public async Task RequestUsesPublicRepositoryAndNoAccountCredentials()
    {
        using var handler = new AppUpdateHttpHandler();
        using var client = new HttpClient(handler);
        using var service = new GitHubAppUpdateService(client);

        await service.CheckForUpdateAsync(new(1, 5, 0, 0));

        Assert.Equal("https://api.github.com/repos/Yohuke-no-Symphony/DanmuNeo/releases/latest",
            handler.RequestUri!.AbsoluteUri);
        Assert.Contains("Yohuke-DanmuNeo/1.5.0.0", handler.UserAgent);
        Assert.Contains("application/vnd.github+json", handler.Accept);
        Assert.False(handler.HasCredentials);
    }

    private static string CreateReleaseJson(string tagName, bool draft = false, bool prerelease = false)
    {
        return JsonSerializer.Serialize(new
        {
            tag_name = tagName,
            html_url = RELEASE_PREFIX + Uri.EscapeDataString(tagName),
            draft,
            prerelease
        });
    }
}