using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>将单次 B 站弹幕请求转换为可判定的发送结果。</summary>
public sealed class BilibiliDanmuSendTransport(HttpClient? httpClient = null) : IDanmuSendTransport
{
    /// <inheritdoc/>
    public async Task<DanmuSendAttemptResult> SendOnceAsync(string roomId, string message, string cookie,
        DanmuRoomConfig config, TimeSpan timeout, CancellationToken cancellationToken)
    {
        DanmuSendDiagnostic? diagnostic = null;
        try
        {
            var parsedRoomId = long.Parse(roomId);
            using var api = new BilibiliApi(cookie, timeout, httpClient);
            diagnostic = DanmuSendDiagnostic.Create(parsedRoomId, message, cookie,
                config.Current.Color, config.Current.Mode, api.DanmuSessionTimestamp);
            var response = await api.SendDanmuAsync(parsedRoomId, message, config.Current.Mode,
                cancellationToken: cancellationToken, color: config.Current.Color);
            var failure = DanmuSendResponseClassifier.Classify(response.Code, response.Message, response.Msg);
            diagnostic = diagnostic.WithResponse(response.Code, response.Message, response.Msg, cookie);
            var errorMessage = !string.IsNullOrWhiteSpace(response.Message) ? response.Message : response.Msg ?? "";
            return new()
            {
                FailureKind = failure,
                ApiCode = response.Code,
                Diagnostic = diagnostic,
                ErrorMessage = failure == DanmuSendFailureKind.None ? "" :
                    AppLogSanitizer.Sanitize(errorMessage, ApiCookieHelper.Parse(cookie).Values),
                StatusKey = failure switch
                {
                    DanmuSendFailureKind.None => "Services.Send.Accepted",
                    DanmuSendFailureKind.Blocked => "Services.Send.Blocked",
                    DanmuSendFailureKind.RateLimited => "Services.Send.RateLimited",
                    _ => null
                }
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failure = exception is HttpRequestException httpError
                ? DanmuSendResponseClassifier.ClassifyHttp(httpError.StatusCode)
                : DanmuSendFailureKind.OutcomeUnknown;
            return new()
            {
                FailureKind = failure,
                ApiCode = null,
                Error = exception,
                Diagnostic = diagnostic is null ? null : diagnostic with
                {
                    HttpStatusCode = exception is HttpRequestException { StatusCode: { } statusCode } ? (int)statusCode : null
                },
                StatusKey = failure == DanmuSendFailureKind.RateLimited ? "Services.Send.RateLimited" : null
            };
        }
    }
}
