using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.I18n;
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
    private readonly IBilibiliAccountSessionService? accountSessions;
    private readonly SemaphoreSlim sendLock = new(1, 1);
    private DateTimeOffset lastSentAt = DateTimeOffset.MinValue;

    /// <summary>
    /// 初始化服务；注入的 HTTP 客户端由调用方管理。
    /// </summary>
    public DanmuSendService(HttpClient? httpClient = null, IBilibiliDanmuConfigService? danmuConfigService = null,
        IBilibiliAccountSessionService? accountSessions = null)
    {
        this.httpClient = httpClient;
        this.danmuConfigService = danmuConfigService ?? new BilibiliDanmuConfigService(httpClient);
        this.accountSessions = accountSessions;
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
            return CreateResult(roomId, message, "", false, statusKey: "Services.Send.NoAccount");
        }

        var cookie = account.Cookie;
        var accountId = account.Id;
        if (accountSessions?.CanSend(accountId, cookie) == false)
        {
            return CreateResult(roomId, message, "", false, statusKey: "Services.Send.AccountUnavailable");
        }

        var credentialToken = accountSessions?.GetCredentialCancellationToken(accountId, cookie) ?? CancellationToken.None;
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, credentialToken);
        var sendToken = linkedSource.Token;
        var enteredLock = false;

        try
        {
            await sendLock.WaitAsync(sendToken);
            enteredLock = true;
            if (accountSessions?.CanSend(accountId, cookie) == false)
            {
                return CreateResult(roomId, message, "", false, statusKey: "Services.Send.AccountUnavailable");
            }

            var waitTime = TimeSpan.FromMilliseconds(settings.SendIntervalMs) - (DateTimeOffset.Now - lastSentAt);

            if (waitTime > TimeSpan.Zero)
            {
                await Task.Delay(waitTime, sendToken);
            }

            var timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);
            var config = await danmuConfigService.GetConfigAsync(roomId, cookie, timeout, sendToken);
            if (accountSessions?.CanSend(accountId, cookie) == false)
            {
                return CreateResult(roomId, message, "", false, statusKey: "Services.Send.AccountUnavailable");
            }

            using var api = new BilibiliApi(cookie, timeout, httpClient);
            var response = await api.SendDanmuAsync(long.Parse(roomId), message, config.Current.Mode,
                cancellationToken: sendToken, color: config.Current.Color);
            lastSentAt = DateTimeOffset.Now;

            var isBlocked = response.Message is "f" || response.Msg is "f";
            if (response.Code == -101)
            {
                accountSessions?.ReportAuthenticationFailure(accountId, cookie);
            }

            if (response.Code == 0 && !isBlocked)
            {
                return CreateResult(roomId, message, "", true, response.Code, statusKey: "Services.Send.Accepted");
            }

            if (isBlocked)
            {
                return CreateResult(roomId, message, "", false, response.Code, statusKey: "Services.Send.Blocked");
            }

            var errorMessage = response.Message ?? response.Msg;
            return errorMessage is not null
                ? CreateResult(roomId, message, errorMessage, false, response.Code)
                : CreateResult(roomId, message, "", false, response.Code,
                    statusKey: "Services.Send.FailedCode", statusArguments: [response.Code]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (credentialToken.IsCancellationRequested)
        {
            return CreateResult(roomId, message, "", false, statusKey: "Services.Send.AccountUnavailable");
        }
        catch (Exception exception)
        {
            return CreateResult(roomId, message, exception.Message, false, error: exception);
        }
        finally
        {
            if (enteredLock)
            {
                sendLock.Release();
            }
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
        int? apiCode = null,
        Exception? error = null,
        string? statusKey = null,
        object[]? statusArguments = null)
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

        if (statusKey is not null)
        {
            record.SetLocalizedStatus(statusKey, statusArguments ?? []);
        }
        else if (error is not null)
        {
            record.SetLocalizedStatus(() => LocalizedExceptionText.Get(error));
        }

        RecordCreated?.Invoke(this, record);
        return new()
        {
            IsAccepted = isAccepted,
            ApiCode = apiCode,
            ErrorMessage = isAccepted ? "" : record.Status,
            ErrorMessageKey = isAccepted ? null : statusKey,
            ErrorMessageArguments = statusArguments ?? [],
            ErrorMessageProvider = error is null ? null : () => LocalizedExceptionText.Get(error),
            Error = error,
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
