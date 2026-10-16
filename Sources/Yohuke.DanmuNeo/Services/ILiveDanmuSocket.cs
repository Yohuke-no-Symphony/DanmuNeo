using Yohuke.DanmuNeo.Apis.Models.Bilibili;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 直播弹幕连接。
/// </summary>
public interface ILiveDanmuSocket
{
    /// <summary>
    /// 接收到普通弹幕时触发。
    /// </summary>
    event EventHandler<BilibiliDanmuMessage>? DanmuReceived;

    /// <summary>
    /// 接收到金币礼物时触发。
    /// </summary>
    event EventHandler<BilibiliGiftMessage>? GiftReceived;

    /// <summary>
    /// 接收到 Super Chat 时触发。
    /// </summary>
    event EventHandler<BilibiliSuperChatMessage>? SuperChatReceived;

    /// <summary>
    /// 接收到大航海购买或续费记录时触发。
    /// </summary>
    event EventHandler<BilibiliGuardPurchaseMessage>? GuardPurchaseReceived;

    /// <summary>
    /// 监听发生错误时触发。
    /// </summary>
    event EventHandler<Exception>? ErrorReceived;

    /// <summary>
    /// 连接中断时触发。
    /// </summary>
    event EventHandler? Disconnected;

    /// <summary>
    /// 连接恢复时触发。
    /// </summary>
    event EventHandler? Recovered;

    /// <summary>
    /// 开始监听。
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);
}
