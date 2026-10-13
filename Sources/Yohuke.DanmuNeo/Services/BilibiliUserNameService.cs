using Yohuke.DanmuNeo.Apis;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 通过公开名片接口查询 B 站用户名。
/// </summary>
public class BilibiliUserNameService : IBilibiliUserNameService
{
    private readonly HttpClient? httpClient;

    /// <summary>
    /// 初始化服务；注入的 HTTP 客户端由调用方管理。
    /// </summary>
    public BilibiliUserNameService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient;
    }

    /// <inheritdoc/>
    public async Task<string> GetUserNameAsync(
        long uid,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(uid, 1);
        using var api = new BilibiliApi(httpClient: httpClient);
        var response = await api.GetUserCardContainerAsync(uid, timeout, cancellationToken);

        if (response.Code != 0)
        {
            var message = !string.IsNullOrWhiteSpace(response.Message)
                ? response.Message
                : !string.IsNullOrWhiteSpace(response.Msg) ? response.Msg : "用户名查询失败";
            throw new InvalidOperationException(message);
        }

        var card = response.Data?.Card;

        if (card is null || card.Uid != uid || string.IsNullOrWhiteSpace(card.Name))
        {
            throw new InvalidOperationException("未获取到用户名");
        }

        return card.Name.Trim();
    }
}
