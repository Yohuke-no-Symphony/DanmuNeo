using Yohuke.DanmuNeo.I18n;
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
            throw LocalizedExceptionText.Create("Services.BilibiliDanmuConfig.CredentialsIncomplete",
                message => new InvalidOperationException(message));
        }

        if (!options.IsValid)
        {
            throw LocalizedExceptionText.Create("Services.BilibiliDanmuConfig.SettingsInvalid",
                message => new InvalidOperationException(message));
        }

        await configLock.WaitAsync(cancellationToken);

        try
        {
            if (!configs.TryGetValue(key, out var config))
            {
                config = await LoadConfigAsync(key.RoomId, key.Cookie, timeout, cancellationToken);
                configs[key] = config;
            }

            var colorChanged = options.Color != config.Current.Color;
            var modeChanged = options.Mode != config.Current.Mode;

            if ((colorChanged && !config.Colors.Any(option => option.Value == options.Color)) ||
                (modeChanged && !config.Modes.Any(option => option.Value == options.Mode)))
            {
                throw LocalizedExceptionText.Create("Services.BilibiliDanmuConfig.SelectionLocked",
                    message => new InvalidOperationException(message));
            }

            if (!colorChanged && !modeChanged)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return config;
            }

            using var api = new BilibiliApi(key.Cookie, timeout, httpClient);

            if (colorChanged)
            {
                config = await SaveConfigChangeAsync(api, key,
                    config with { Current = config.Current with { Color = options.Color } }, timeout, cancellationToken,
                    color: $"0x{options.Color.ToString("X6", CultureInfo.InvariantCulture)}");
            }

            if (modeChanged)
            {
                config = await SaveConfigChangeAsync(api, key,
                    config with { Current = config.Current with { Mode = options.Mode } }, timeout, cancellationToken,
                    mode: options.Mode);
            }

            return config;
        }
        finally
        {
            configLock.Release();
        }
    }

    private async Task<DanmuRoomConfig> SaveConfigChangeAsync(BilibiliApi api, (long RoomId, string Cookie) key,
        DanmuRoomConfig confirmed, TimeSpan? timeout, CancellationToken cancellationToken,
        string? color = null, int? mode = null)
    {
        ApiResponse<JToken> response;

        try
        {
            response = await api.SetDanmuConfigAsync(key.RoomId, color, mode,
                timeout: timeout, cancellationToken: cancellationToken);
        }
        catch
        {
            configs.Remove(key);
            throw;
        }

        EnsureSuccess(response.Code, response.Message, response.Msg, "Services.BilibiliDanmuConfig.SaveFailed");
        configs[key] = confirmed;
        cancellationToken.ThrowIfCancellationRequested();
        return confirmed;
    }

    private async Task<DanmuRoomConfig> LoadConfigAsync(long roomId, string cookie, TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        using var api = new BilibiliApi(cookie, timeout, httpClient);
        var response = await api.GetDanmuConfigAsync(roomId, timeout: timeout, cancellationToken: cancellationToken);
        EnsureSuccess(response.Code, response.Message, response.Msg, "Services.BilibiliDanmuConfig.OptionsReadFailed");
        var userResponse = await api.GetUserInfoAsync(roomId, timeout: timeout, cancellationToken: cancellationToken);
        EnsureSuccess(userResponse.Code, userResponse.Message, userResponse.Msg, "Services.BilibiliDanmuConfig.CurrentReadFailed");
        return BilibiliDanmuConfigSelector.Select(response.Data, userResponse.Data);
    }

    private static (long RoomId, string Cookie) CreateKey(string roomId, string cookie)
    {
        if (!long.TryParse(roomId, out var parsedRoomId) || parsedRoomId <= 0)
        {
            throw LocalizedExceptionText.Create("Services.BilibiliDanmuConfig.RoomIdInvalid",
                message => new InvalidOperationException(message));
        }

        var normalizedCookie = ApiCookieHelper.CreateBilibiliCookie(cookie);

        if (string.IsNullOrWhiteSpace(ApiCookieHelper.GetValue(normalizedCookie, "SESSDATA")))
        {
            throw LocalizedExceptionText.Create("Services.BilibiliDanmuConfig.AccountMissing",
                message => new InvalidOperationException(message));
        }

        return (parsedRoomId, normalizedCookie);
    }

    private static void EnsureSuccess(int code, string? message, string? msg, string fallbackKey)
    {
        if (code == 0)
        {
            return;
        }

        var error = !string.IsNullOrWhiteSpace(message) ? message : msg;
        throw string.IsNullOrWhiteSpace(error)
            ? LocalizedExceptionText.Create("Services.BilibiliDanmuConfig.FailureWithCode",
                text => new InvalidOperationException(text), new Func<string>(() => Localization.Get(fallbackKey)), code)
            : new InvalidOperationException(error);
    }
}
