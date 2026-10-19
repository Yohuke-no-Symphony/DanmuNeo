using System.Threading;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

public class FakeDanmuSendService : IDanmuSendService
{
    public event EventHandler<DanmuFeedItem>? RecordCreated;

    public List<string> SentMessages { get; } = [];

    public List<BilibiliAccount?> SentAccounts { get; } = [];

    public List<DanmuFeedItem> Records { get; } = [];

    public bool IsAccepted { get; set; } = true;

    public string ErrorMessage { get; set; } = "测试发送失败";

    public int? FailureApiCode { get; set; } = -1;

    public Queue<bool> AcceptanceResults { get; } = [];

    public List<string>? MessageParts { get; set; }

    /// <inheritdoc/>
    public Task<DanmuDeliveryResult> SendAsync(DanmuSendRequest request, BilibiliAccount? account,
        AppSettings settings, DanmuDeliveryProgress? continuation = null, CancellationToken cancellationToken = default)
    {
        return TestDanmuDelivery.SendAsync(request, account, settings, continuation, cancellationToken,
            SendAsync, record => RecordCreated?.Invoke(this, record), MessageParts);
    }

    public Task<DanmuSendResult> SendAsync(
        string roomId,
        string message,
        BilibiliAccount? account,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        var isAccepted = AcceptanceResults.Count > 0 ? AcceptanceResults.Dequeue() : IsAccepted;
        SentMessages.Add(message);
        SentAccounts.Add(account);
        var record = new DanmuFeedItem
        {
            Time = DateTimeOffset.Now,
            UserName = roomId,
            Content = message,
            IsLocalRecord = true,
            IsSendAccepted = isAccepted,
            Status = isAccepted ? "接口已接受" : ErrorMessage
        };
        Records.Add(record);
        return Task.FromResult(new DanmuSendResult
        {
            IsAccepted = isAccepted,
            ApiCode = isAccepted ? 0 : FailureApiCode,
            ErrorMessage = isAccepted ? "" : ErrorMessage,
            Record = record
        });
    }
}
