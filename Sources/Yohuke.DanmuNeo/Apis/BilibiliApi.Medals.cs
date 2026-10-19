using Newtonsoft.Json.Linq;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Apis.Models.Common;

namespace Yohuke.DanmuNeo.Apis;

public partial class BilibiliApi
{
    /// <summary>查询经过验证的账号UID对应的粉丝牌墙。</summary>
    public Task<ApiResponse<BilibiliMedalWallData>> GetMedalWallAsync(long accountUid,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<ApiResponse<BilibiliMedalWallData>>(
            "https://api.live.bilibili.com/xlive/web-ucenter/user/MedalWall",
            new Dictionary<string, object?> { ["target_id"] = accountUid }, CreateAccountHeaders(0), timeout,
            cancellationToken);
    }

    /// <summary>佩戴拥有的粉丝牌。</summary>
    public Task<ApiResponse<JToken>> WearMedalAsync(long medalId, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return PostMedalAsync("wear", medalId, timeout, cancellationToken);
    }

    /// <summary>摘下当前佩戴的粉丝牌。</summary>
    public Task<ApiResponse<JToken>> TakeOffMedalAsync(long medalId, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return PostMedalAsync("take_off", medalId, timeout, cancellationToken);
    }

    private Task<ApiResponse<JToken>> PostMedalAsync(string operation, long medalId, TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        return PostFormJsonAsync<ApiResponse<JToken>>(
            $"https://api.live.bilibili.com/xlive/app-ucenter/v1/fansMedal/{operation}",
            new Dictionary<string, object?>
            {
                ["medal_id"] = medalId, ["csrf"] = GetCsrf(0), ["csrf_token"] = GetCsrf(0)
            }, CreateAccountHeaders(0), timeout, cancellationToken);
    }
}
