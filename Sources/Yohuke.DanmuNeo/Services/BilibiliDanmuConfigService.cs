using System.Globalization;
using Newtonsoft.Json.Linq;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Apis.Models.Common;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 读取和保存 B 站弹幕配置，并按房间与 Cookie 身份缓存已确认值。
/// </summary>
public class BilibiliDanmuConfigService : IBilibiliDanmuConfigService
{
    private readonly HttpClient? httpClient;
    private readonly SemaphoreSlim configLock = new(1, 1);
    private readonly Dictionary<(long RoomId, string Cookie), DanmuRoomConfig> configs = [];

    /// <summary>
    /// 初始化服务；注入的 HTTP 客户端由调用方管理。
    /// </summary>
    public BilibiliDanmuConfigService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient;
    }

    /// <inheritdoc/>
    public async Task<DanmuRoomConfig> GetConfigAsync(
        string roomId,
        string cookie,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default,
        bool forceRefresh = false)
    {
        var key = CreateKey(roomId, cookie);
        await configLock.WaitAsync(cancellationToken);

        try
        {
            if (!forceRefresh && configs.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var config = await LoadConfigAsync(key.RoomId, key.Cookie, timeout, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            configs[key] = config;
            return config;
        }
        finally
        {
            configLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<DanmuRoomConfig> SetConfigAsync(
        string roomId,
        string cookie,
        DanmuSendOptions options,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var key = CreateKey(roomId, cookie);

        if (string.IsNullOrWhiteSpace(ApiCookieHelper.GetValue(key.Cookie, "bili_jct")))
        {
            throw new InvalidOperationException("账号登录凭据不完整，请重新登录");
        }

        if (!options.IsValid)
        {
            throw new InvalidOperationException("弹幕颜色或位置格式错误");
        }

        await configLock.WaitAsync(cancellationToken);

        try
        {
            if (!configs.TryGetValue(key, out var config))
            {
                config = await LoadConfigAsync(key.RoomId, key.Cookie, timeout, cancellationToken);
                configs[key] = config;
            }

            if (!config.Colors.Any(option => option.Value == options.Color) ||
                !config.Modes.Any(option => option.Value == options.Mode))
            {
                throw new InvalidOperationException("当前账号尚未解锁所选弹幕颜色或位置");
            }

            using var api = new BilibiliApi(key.Cookie, timeout, httpClient);
            ApiResponse<JToken> response;

            try
            {
                response = await api.SetDanmuConfigAsync(key.RoomId,
                    $"0x{options.Color.ToString("X6", CultureInfo.InvariantCulture)}", options.Mode,
                    timeout: timeout, cancellationToken: cancellationToken);
            }
            catch
            {
                configs.Remove(key);
                throw;
            }

            EnsureSuccess(response.Code, response.Message, response.Msg, "弹幕配置保存失败");
            var confirmed = config with { Current = options };
            configs[key] = confirmed;
            cancellationToken.ThrowIfCancellationRequested();
            return confirmed;
        }
        finally
        {
            configLock.Release();
        }
    }

    private async Task<DanmuRoomConfig> LoadConfigAsync(long roomId, string cookie, TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        using var api = new BilibiliApi(cookie, timeout, httpClient);
        var response = await api.GetDanmuConfigAsync(roomId, timeout: timeout, cancellationToken: cancellationToken);
        EnsureSuccess(response.Code, response.Message, response.Msg, "弹幕可用选项读取失败");
        var userResponse = await api.GetUserInfoAsync(roomId, timeout: timeout, cancellationToken: cancellationToken);
        EnsureSuccess(userResponse.Code, userResponse.Message, userResponse.Msg, "当前弹幕配置读取失败");
        return BilibiliDanmuConfigSelector.Select(response.Data, userResponse.Data);
    }

    private static (long RoomId, string Cookie) CreateKey(string roomId, string cookie)
    {
        if (!long.TryParse(roomId, out var parsedRoomId) || parsedRoomId <= 0)
        {
            throw new InvalidOperationException("房间号格式错误");
        }

        var normalizedCookie = ApiCookieHelper.CreateBilibiliCookie(cookie);

        if (string.IsNullOrWhiteSpace(ApiCookieHelper.GetValue(normalizedCookie, "SESSDATA")))
        {
            throw new InvalidOperationException("未配置登录账号");
        }

        return (parsedRoomId, normalizedCookie);
    }

    private static void EnsureSuccess(int code, string? message, string? msg, string fallback)
    {
        if (code == 0)
        {
            return;
        }

        var error = !string.IsNullOrWhiteSpace(message) ? message : msg;
        throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"{fallback}：{code}" : error);
    }
}
