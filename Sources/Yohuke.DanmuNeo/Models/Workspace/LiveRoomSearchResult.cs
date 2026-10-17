using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// 可添加到工作区的主播搜索结果。
/// </summary>
public class LiveRoomSearchResult : ObservableObject
{
    /// <summary>
    /// 直播间 ID。
    /// </summary>
    public string RoomId { get; init; } = "";

    /// <summary>
    /// 主播 UID。
    /// </summary>
    public string OwnerUid { get; init; } = "";

    /// <summary>
    /// 不含搜索高亮的主播昵称。
    /// </summary>
    public string OwnerName { get; init; } = "";

    /// <summary>
    /// 直播标题。
    /// </summary>
    public string Title { get; init; } = "";

    /// <summary>
    /// 是否正在直播。
    /// </summary>
    public bool IsLive { get; init; }

    /// <summary>
    /// 列表中的房间与直播状态说明。
    /// </summary>
    public string DetailLine => Localization.Format("Room.Search.Detail", RoomId,
        Localization.Get(IsLive ? "Room.Search.Live" : "Room.Search.Offline"));

    internal void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DetailLine));
    }
}
