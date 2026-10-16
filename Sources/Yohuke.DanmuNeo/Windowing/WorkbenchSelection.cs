using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Windowing;

/// <summary>
/// 将延迟的界面操作绑定到发起时的直播间身份和选择代次。
/// </summary>
public sealed record WorkbenchSelection(LiveRoomTabViewModel Room, string RoomId, long Generation);
