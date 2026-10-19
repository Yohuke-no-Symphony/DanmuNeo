using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>统一排队、分段并投递逻辑弹幕。</summary>
public interface IDanmuSendService
{
    /// <summary>分段产生终态时通知；相同记录 ID 表示更新已有历史。</summary>
    event EventHandler<DanmuFeedItem>? RecordCreated;

    /// <summary>发送全部未接受正文，可在内容与身份不变时继续先前操作。</summary>
    Task<DanmuDeliveryResult> SendAsync(DanmuSendRequest request, BilibiliAccount? account,
        AppSettings settings, DanmuDeliveryProgress? continuation = null, CancellationToken cancellationToken = default);
}
