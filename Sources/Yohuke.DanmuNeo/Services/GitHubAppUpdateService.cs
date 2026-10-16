using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Yohuke.DanmuNeo.Apis.Models.GitHub;
using Yohuke.DanmuNeo.Models.Updates;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 通过 GitHub 的最新正式发布接口检查应用更新。
/// </summary>
public sealed class GitHubAppUpdateService : IAppUpdateService, IDisposable
{
    private const string LATEST_RELEASE_URL =
        "https://api.github.com/repos/Yohuke-no-Symphony/DanmuNeo/releases/latest";

    private const string RELEASE_PAGE_PREFIX =
        "https://github.com/Yohuke-no-Symphony/DanmuNeo/releases/tag/";

    private const int REQUEST_TIMEOUT_SECONDS = 10;

    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;

    /// <summary>
    /// 初始化检查服务；注入的 HTTP 客户端由调用方管理。
    /// </summary>
    public GitHubAppUpdateService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new();
        disposeHttpClient = httpClient is null;
    }

    /// <inheritdoc/>
    public async Task<AppUpdateInfo?> CheckForUpdateAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(REQUEST_TIMEOUT_SECONDS));
        using var request = new HttpRequestMessage(HttpMethod.Get, LATEST_RELEASE_URL);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Yohuke-DanmuNeo", currentVersion.ToString()));

        using var response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeoutSource.Token).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
            stream, cancellationToken: timeoutSource.Token).ConfigureAwait(false);

        if (release is null || release.IsDraft || release.IsPrerelease ||
            string.IsNullOrWhiteSpace(release.TagName))
        {
            return null;
        }

        var tagName = release.TagName.Trim();
        var versionText = tagName.StartsWith('v') || tagName.StartsWith('V') ? tagName[1..] : tagName;

        if (!Version.TryParse(versionText, out var releaseVersion) ||
            NormalizeVersion(releaseVersion) <= NormalizeVersion(currentVersion) ||
            !Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out var releaseUri) ||
            !releaseUri.AbsoluteUri.StartsWith(RELEASE_PAGE_PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new(NormalizeVersion(releaseVersion), tagName, releaseUri);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposeHttpClient)
        {
            httpClient.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private static Version NormalizeVersion(Version version)
    {
        return new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
    }
}