using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Apis.Models.Common;

namespace Yohuke.DanmuNeo.Apis;

public partial class BilibiliApi
{
    /// <summary>
    /// 获取 B 站服务器当前 Unix 秒数，此接口不发送账号 Cookie。
    /// </summary>
    public Task<ApiResponse<BilibiliServerTimeData>> GetServerTimeAsync(
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<ApiResponse<BilibiliServerTimeData>>(
            "https://api.bilibili.com/x/click-interface/click/now",
            headers: CreateDefaultHeaders(),
            timeout: timeout,
            cancellationToken: cancellationToken);
    }
}
