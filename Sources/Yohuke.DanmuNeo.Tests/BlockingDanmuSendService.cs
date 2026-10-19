using System.Threading;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

internal class BlockingDanmuSendService : IDanmuSendService
{
    private readonly TaskCompletionSource firstSendStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource firstSendRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int sendCount;

    public event EventHandler<DanmuFeedItem>? RecordCreated;

    public Task FirstSendStarted => firstSendStarted.Task;

    public bool IsAccepted { get; set; } = true;

    /// <summary>
    /// 在发送解除阻塞后抛出的异常。
    /// </summary>
    public Exception? Exception { get; set; }

    public void ReleaseFirstSend()
    {
        firstSendRelease.TrySetResult();
    }

    /// <inheritdoc/>
    public Task<DanmuDeliveryResult> SendAsync(DanmuSendRequest request, BilibiliAccount? account,
        AppSettings settings, DanmuDeliveryProgress? continuation = null, CancellationToken cancellationToken = default)
    {
        return TestDanmuDelivery.SendAsync(request, account, settings, continuation, cancellationToken,
            SendAsync, record => RecordCreated?.Invoke(this, record));
    }

    public async Task<DanmuSendResult> SendAsync(
        string roomId,
        string message,
        BilibiliAccount? account,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        var isAccepted = IsAccepted;
        var exception = Exception;

        if (Interlocked.Increment(ref sendCount) == 1)
        {
            firstSendStarted.TrySetResult();
            await firstSendRelease.Task;
        }

        if (exception is not null)
        {
            throw exception;
        }

        var record = new DanmuFeedItem
        {
            UserName = roomId,
            Content = message,
            IsLocalRecord = true,
            IsSendAccepted = isAccepted,
            Status = isAccepted ? "接口已接受" : "测试接口拒绝"
        };
        return new()
        {
            IsAccepted = isAccepted,
            ApiCode = isAccepted ? 0 : -1,
            ErrorMessage = isAccepted ? "" : "测试接口拒绝",
            Record = record
        };
    }
}
