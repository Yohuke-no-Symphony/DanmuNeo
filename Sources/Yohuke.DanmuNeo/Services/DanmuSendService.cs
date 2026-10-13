using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 统一管理弹幕发送队列。
/// </summary>
public class DanmuSendService : IDanmuSendService
{
    private readonly HttpClient? httpClient;
    private readonly IBilibiliDanmuConfigService danmuConfigService;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private DateTimeOffset lastSentAt = DateTimeOffset.MinValue;

    /// <summary>
    /// 初始化服务；注入的 HTTP 客户端由调用方管理。
    /// </summary>
    public DanmuSendService(HttpClient? httpClient = null, IBilibiliDanmuConfigService? danmuConfigService = null)
    {
        this.httpClient = httpClient;
        this.danmuConfigService = danmuConfigService ?? new BilibiliDanmuConfigService(httpClient);
    }

    /// <summary>
    /// 发送记录产生时触发。
    /// </summary>
    public event EventHandler<DanmuFeedItem>? RecordCreated;

    /// <summary>
    /// 发送弹幕。
    /// </summary>
    public async Task<DanmuSendResult> SendAsync(
        string roomId,
        string message,
        BilibiliAccount? account,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (account is null || string.IsNullOrWhiteSpace(account.Cookie))
        {
            return CreateResult(roomId, message, "未配置账号", false);
        }

        var cookie = account.Cookie;
        await sendLock.WaitAsync(cancellationToken);

        try
        {
            var waitTime = TimeSpan.FromMilliseconds(settings.SendIntervalMs) - (DateTimeOffset.Now - lastSentAt);

            if (waitTime > TimeSpan.Zero)
            {
                await Task.Delay(waitTime, cancellationToken);
            }

            var timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            var config = await danmuConfigService.GetConfigAsync(roomId, cookie, timeout, cancellationToken);
            using var api = new BilibiliApi(cookie, timeout, httpClient);
            var response = await api.SendDanmuAsync(long.Parse(roomId), message, config.Current.Mode,
                cancellationToken: cancellationToken, color: config.Current.Color);
            lastSentAt = DateTimeOffset.Now;

            var isBlocked = response.Message is "f" || response.Msg is "f";

            if (response.Code == 0 && !isBlocked)
            {
                return CreateResult(roomId, message, "接口已接受", true, response.Code);
            }

            var errorMessage = isBlocked
                ? "弹幕被屏蔽"
                : response.Message ?? response.Msg ?? $"发送失败：{response.Code}";
            return CreateResult(roomId, message, errorMessage, false, response.Code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return CreateResult(roomId, message, exception.Message, false);
        }
        finally
        {
            sendLock.Release();
        }
    }

    /// <summary>
    /// 按最大长度切分弹幕。
    /// </summary>
    public List<string> SplitMessage(string message, int maxLength)
    {
        if (message.Length <= maxLength)
        {
            return [message];
        }

        var result = new List<string>();
        var remaining = message;

        while (remaining.Length > maxLength)
        {
            var cutIndex = FindCutIndex(remaining, maxLength);
            result.Add(remaining[..cutIndex]);
            remaining = "…" + remaining[cutIndex..];
        }

        if (remaining.Length > 0)
        {
            result.Add(remaining);
        }

        return result;
    }

    private DanmuSendResult CreateResult(
        string roomId,
        string message,
        string status,
        bool isAccepted,
        int? apiCode = null)
    {
        var record = new DanmuFeedItem
        {
            Time = DateTimeOffset.Now,
            UserName = roomId,
            Content = message,
            IsLocalRecord = true,
            IsSendAccepted = isAccepted,
            Status = status
        };
        RecordCreated?.Invoke(this, record);
        return new()
        {
            IsAccepted = isAccepted,
            ApiCode = apiCode,
            ErrorMessage = isAccepted ? "" : status,
            Record = record
        };
    }

    private static int FindCutIndex(string message, int maxLength)
    {
        for (var index = maxLength - 1; index > Math.Max(1, maxLength / 2); index--)
        {
            if (" 　/，。：！？,.:!?)）】".Contains(message[index]))
            {
                return index + 1;
            }
        }

        return maxLength;
    }
}
