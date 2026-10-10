using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>
/// 直播弹幕展示项。
/// </summary>
public class DanmuFeedItem : ObservableObject
{
    private const int MAX_MEDAL_NAME_LENGTH = 3;

    /// <summary>
    /// 接收此弹幕时的直播间 ID。
    /// </summary>
    public string RoomId { get; set; } = "";

    /// <summary>
    /// 发送者是否为房管。
    /// </summary>
    public bool IsRoomAdmin { get; set; }

    /// <summary>
    /// 发送者佩戴的粉丝牌名称。
    /// </summary>
    public string MedalName { get; set; } = "";

    /// <summary>
    /// 粉丝牌等级。
    /// </summary>
    public int MedalLevel { get; set; }

    /// <summary>
    /// 是否有可展示的粉丝牌。
    /// </summary>
    public bool HasMedal => !string.IsNullOrWhiteSpace(MedalName) && MedalLevel > 0;

    /// <summary>
    /// 粉丝牌名称与等级。
    /// </summary>
    public string MedalText => HasMedal ? $"{MedalName} {MedalLevel}" : "";

    /// <summary>
    /// 名称最多展示三个文字元素的粉丝牌及等级。
    /// </summary>
    public string MedalDisplayText
    {
        get
        {
            if (!HasMedal)
            {
                return "";
            }

            var name = new StringInfo(MedalName);
            var displayName =
                name.SubstringByTextElements(0, Math.Min(MAX_MEDAL_NAME_LENGTH, name.LengthInTextElements));
            return $"{displayName} {MedalLevel}";
        }
    }

    /// <summary>
    /// 发送时间。
    /// </summary>
    public DateTimeOffset Time { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// 用户名。
    /// </summary>
    public string UserName { get; set; } = "";

    /// <summary>
    /// 用户 UID。
    /// </summary>
    public string UserUid { get; set; } = "";

    /// <summary>
    /// 弹幕内容。
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// 是否为当前应用发送记录。
    /// </summary>
    public bool IsLocalRecord { get; set; }

    private string status = "";

    /// <summary>
    /// 是否已被 B 站接口接受。
    /// </summary>
    public bool IsSendAccepted { get; set; }

    /// <summary>
    /// 状态文本。
    /// </summary>
    public string Status
    {
        get => status;
        set
        {
            if (SetProperty(ref status, value))
            {
                OnPropertyChanged(nameof(DetailLine));
            }
        }
    }

    /// <summary>
    /// 展示用时间。
    /// </summary>
    public string DisplayTime => Time.ToLocalTime().ToString("HH:mm:ss");

    /// <summary>
    /// 展示用附加信息。
    /// </summary>
    public string DetailLine => string.IsNullOrWhiteSpace(Status)
        ? DisplayTime
        : $"{DisplayTime} · {Status}";
}
