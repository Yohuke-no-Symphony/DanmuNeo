using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>统一管理有序逻辑发送、分段、限速、限频重试与进度。</summary>
public class DanmuSendService : IDanmuSendService
{
    private readonly IBilibiliDanmuConfigService danmuConfigService;
    private readonly IDanmuSendTransport transport;
    private readonly TimeProvider timeProvider;
    private readonly IAppLogService? log;
    private readonly IBilibiliAccountSessionService? accountSessions;
    private readonly DanmuSendQueue sendQueue = new();
    private DateTimeOffset lastSentAt = DateTimeOffset.MinValue;

    /// <summary>初始化发送管线；注入的 HTTP 客户端与日志由调用方管理。</summary>
    public DanmuSendService(HttpClient? httpClient = null, IBilibiliDanmuConfigService? danmuConfigService = null,
        IDanmuSendTransport? transport = null, TimeProvider? timeProvider = null, IAppLogService? log = null,
        IBilibiliAccountSessionService? accountSessions = null)
    {
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.danmuConfigService = danmuConfigService ?? new BilibiliDanmuConfigService(httpClient, this.timeProvider);
        this.transport = transport ?? new BilibiliDanmuSendTransport(httpClient);
        this.log = log;
        this.accountSessions = accountSessions;
    }

    /// <inheritdoc/>
    public event EventHandler<DanmuFeedItem>? RecordCreated;

    /// <inheritdoc/>
    public async Task<DanmuDeliveryResult> SendAsync(DanmuSendRequest request, BilibiliAccount? account,
        AppSettings settings, DanmuDeliveryProgress? continuation = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sendingAccount = Snapshot(account);
        var progress = continuation is not null && continuation.Matches(request, sendingAccount)
            ? continuation
            : new DanmuDeliveryProgress(request, sendingAccount);
        request = progress.Request;
        var intervalMs = Math.Max(0, settings.SendIntervalMs);
        var retryWhenLimited = settings.RetryWhenRateLimited;
        var timeoutSeconds = settings.TimeoutSeconds;
        var credentialToken = sendingAccount is null ? CancellationToken.None :
            accountSessions?.GetCredentialCancellationToken(sendingAccount.Id, sendingAccount.Cookie) ?? CancellationToken.None;
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, credentialToken);
        var sendToken = linkedSource.Token;
        try
        {
            if (sendingAccount is null || string.IsNullOrWhiteSpace(sendingAccount.Cookie))
            {
                RecordLocalFailure(progress, "Services.Send.NoAccount");
                return new() { Progress = progress };
            }
            if (request.Contents.Count == 0 || request.Contents.Any(content => string.IsNullOrWhiteSpace(content.Body)))
            {
                RecordLocalFailure(progress, "Services.Send.EmptyBody");
                return new() { Progress = progress };
            }
            if (progress.IsComplete && progress.Results.All(result => result.IsAccepted))
            {
                return new() { Progress = progress };
            }
            if (!ValidateSendingAccount(sendingAccount, progress))
            {
                return new() { Progress = progress };
            }
            using var queueTicket = sendQueue.Enqueue();
            await queueTicket.Ready.WaitAsync(sendToken);
            sendToken.ThrowIfCancellationRequested();
            if (!ValidateSendingAccount(sendingAccount, progress))
            {
                return new() { Progress = progress };
            }
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);
            var config = await danmuConfigService.GetConfigAsync(request.RoomId, sendingAccount.Cookie,
                timeout, sendToken);
            if (config.Length <= 0)
            {
                config = await danmuConfigService.GetConfigAsync(request.RoomId, sendingAccount.Cookie,
                    timeout, sendToken, forceRefresh: true);
            }
            if (config.Length <= 0)
            {
                RecordLocalFailure(progress, "Services.Send.LengthUnavailable");
                return new() { Progress = progress };
            }
            for (var contentIndex = 0; contentIndex < request.Contents.Count; contentIndex++)
            {
                var content = request.Contents[contentIndex];
                while (progress.GetAcceptedLength(contentIndex) < content.Body.Length)
                {
                    sendToken.ThrowIfCancellationRequested();
                    var segment = DanmuMessageSegmenter.Next(content, progress.GetAcceptedLength(contentIndex), config.Length);
                    var attemptCount = 0;
                    DanmuSendAttemptResult attempt;
                    do
                    {
                        await WaitForSendIntervalAsync(intervalMs, sendToken);
                        sendToken.ThrowIfCancellationRequested();
                        if (!ValidateSendingAccount(sendingAccount, progress))
                        {
                            return new() { Progress = progress };
                        }
                        attemptCount++;
                        try
                        {
                            attempt = await transport.SendOnceAsync(request.RoomId, segment.Message,
                                sendingAccount.Cookie, config, timeout, sendToken);
                        }
                        finally
                        {
                            lastSentAt = timeProvider.GetUtcNow();
                        }
                        var retryDelay = DanmuSendRetryPolicy.GetDelay(attempt.FailureKind, retryWhenLimited, attemptCount, intervalMs);
                        if (retryDelay is null)
                        {
                            break;
                        }
                        log?.Information(Localization.Get("Services.Send.LogSource"),
                            Localization.Format("Services.Send.RetryScheduled", request.RoomId, attemptCount, retryDelay.Value.TotalSeconds));
                        await Task.Delay(retryDelay.Value, timeProvider, sendToken);
                    } while (true);
                    if (attempt.FailureKind != DanmuSendFailureKind.None)
                    {
                        sendToken.ThrowIfCancellationRequested();
                    }
                    var result = CreateResult(request.RoomId, segment.Message,
                        progress.GetRecordId(contentIndex, segment.BodyStart), attempt, attemptCount);
                    progress.Record(contentIndex, segment, result);
                    RecordCreated?.Invoke(this, result.Record);
                    if (attempt.ApiCode == -101)
                    {
                        accountSessions?.ReportAuthenticationFailure(sendingAccount.Id, sendingAccount.Cookie);
                    }
                    if (!result.IsAccepted)
                    {
                        return new() { Progress = progress };
                    }
                }
            }
            return new() { Progress = progress };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (credentialToken.IsCancellationRequested)
        {
            RecordLocalFailure(progress, "Services.Send.AccountUnavailable");
            return new() { Progress = progress };
        }
        catch (BilibiliAuthenticationException exception)
        {
            RecordLocalFailure(progress, "Services.Send.AccountUnavailable", exception);
            if (sendingAccount is not null)
            {
                accountSessions?.ReportAuthenticationFailure(sendingAccount.Id, sendingAccount.Cookie);
            }
            return new() { Progress = progress };
        }
        catch (Exception exception)
        {
            RecordLocalFailure(progress, null, exception);
            return new() { Progress = progress };
        }
    }

    /// <summary>发送不带独立符号的单语言内容，并返回最后一段的结果。</summary>
    public async Task<DanmuSendResult> SendAsync(string roomId, string message, BilibiliAccount? account,
        AppSettings settings, CancellationToken cancellationToken = default)
    {
        var delivery = await SendAsync(new() { RoomId = roomId, Contents = [new() { Body = message }] },
            account, settings, cancellationToken: cancellationToken);
        return delivery.Results.Last();
    }

    private async Task WaitForSendIntervalAsync(int intervalMs, CancellationToken cancellationToken)
    {
        var remaining = TimeSpan.FromMilliseconds(intervalMs) - (timeProvider.GetUtcNow() - lastSentAt);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, timeProvider, cancellationToken);
        }
    }

    private bool ValidateSendingAccount(BilibiliAccount account, DanmuDeliveryProgress progress)
    {
        if (accountSessions?.CanSend(account.Id, account.Cookie) != false)
        {
            return true;
        }

        RecordLocalFailure(progress, "Services.Send.AccountUnavailable");
        return false;
    }

    private void RecordLocalFailure(DanmuDeliveryProgress progress, string? statusKey, Exception? error = null)
    {
        var index = progress.Request.Contents.Select((content, contentIndex) => contentIndex)
            .FirstOrDefault(contentIndex => progress.GetAcceptedLength(contentIndex) < progress.Request.Contents[contentIndex].Body.Length);
        var content = progress.Request.Contents.ElementAtOrDefault(index) ?? new();
        var start = progress.Request.Contents.Count == 0 ? 0 : progress.GetAcceptedLength(index);
        var segment = new DanmuSendSegment { BodyStart = start, Message = content.Prefix + content.Body[start..] + content.Suffix };
        var result = CreateResult(progress.Request.RoomId, segment.Message, progress.GetRecordId(index, start),
            new() { FailureKind = DanmuSendFailureKind.LocalFailure, ApiCode = null, StatusKey = statusKey, Error = error }, 0);
        progress.Record(index, segment, result);
        RecordCreated?.Invoke(this, result.Record);
    }

    private DanmuSendResult CreateResult(string roomId, string message, string recordId,
        DanmuSendAttemptResult attempt, int attemptCount)
    {
        var accepted = attempt.FailureKind == DanmuSendFailureKind.None;
        var record = new DanmuFeedItem
        {
            Time = timeProvider.GetUtcNow(), RoomId = roomId, UserName = roomId, Content = message,
            SendRecordId = recordId, IsLocalRecord = true, IsSendAccepted = accepted, Status = attempt.ErrorMessage
        };
        var key = attempt.StatusKey;
        object[] arguments = [];
        if (key is null && attempt.Error is null && string.IsNullOrWhiteSpace(attempt.ErrorMessage))
        {
            key = accepted ? "Services.Send.Accepted" : "Services.Send.FailedCode";
            arguments = accepted ? [] : [attempt.ApiCode ?? 0];
        }
        if (key is not null)
        {
            record.SetLocalizedStatus(key, arguments);
        }
        else if (attempt.Error is not null)
        {
            record.SetLocalizedStatus(() => LocalizedExceptionText.Get(attempt.Error));
        }
        return new()
        {
            IsAccepted = accepted, FailureKind = attempt.FailureKind, AttemptCount = attemptCount,
            ApiCode = attempt.ApiCode, ErrorMessage = accepted ? "" : record.Status, Error = attempt.Error,
            ErrorMessageKey = accepted ? null : key, ErrorMessageArguments = arguments,
            ErrorMessageProvider = attempt.Error is null || key is not null ? null : () => LocalizedExceptionText.Get(attempt.Error),
            Record = record
        };
    }

    private static BilibiliAccount? Snapshot(BilibiliAccount? account)
    {
        return account is null ? null : new()
        {
            Id = account.Id, Name = account.Name, Cookie = account.Cookie, IsGlobalDefault = account.IsGlobalDefault
        };
    }
}
