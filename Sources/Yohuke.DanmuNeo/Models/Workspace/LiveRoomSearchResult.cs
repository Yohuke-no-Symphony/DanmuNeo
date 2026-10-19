using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// 可添加到工作区的主播搜索结果。
/// </summary>
public class LiveRoomSearchResult : ObservableObject
{
    private LiveRoomSearchSource sources = LiveRoomSearchSource.Nickname;

    /// <summary>
    /// 直播间 ID。
    /// </summary>
    public string RoomId { get; init; } = "";

    /// <summary>短房间号；接口未提供时为空。</summary>
    public string ShortRoomId { get; init; } = "";

    /// <summary>合并到当前房间的查找来源。</summary>
    public LiveRoomSearchSource Sources
    {
        get => sources;
        set
        {
            if (SetProperty(ref sources, value))
            {
                OnPropertyChanged(nameof(SourceLabel));
            }
        }
    }

    /// <summary>查找来源说明。</summary>
    public string SourceLabel => GetSourceLabel(Sources);

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
    public string DetailLine => Localization.Format("Room.Search.Detail", RoomId, OwnerUid,
        Localization.Get(IsLive ? "Room.Search.Live" : "Room.Search.Offline"));

    internal static string GetSourceLabel(LiveRoomSearchSource source)
    {
        return string.Join(" · ", Enum.GetValues<LiveRoomSearchSource>()
            .Where(value => value != LiveRoomSearchSource.None && source.HasFlag(value))
            .Select(value => Localization.Get($"Room.Search.Source.{value}")));
    }

    internal void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DetailLine));
        OnPropertyChanged(nameof(SourceLabel));
    }
}
