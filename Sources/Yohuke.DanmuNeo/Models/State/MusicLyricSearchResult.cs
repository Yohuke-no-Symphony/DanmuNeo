using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Models.State;

/// <summary>
/// 音乐 API 歌词搜索结果。
/// </summary>
public class MusicLyricSearchResult : ObservableObject
{
    /// <summary>
    /// 来源平台。
    /// </summary>
    public string Source { get; set; } = "";

    /// <summary>
    /// 来源歌曲 ID。
    /// </summary>
    public string SourceSongId { get; set; } = "";

    /// <summary>
    /// 歌曲 MID。
    /// </summary>
    public string SongMid { get; set; } = "";

    /// <summary>
    /// 歌曲名。
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// 歌手。
    /// </summary>
    public string Artist { get; set; } = "";

    /// <summary>
    /// 专辑。
    /// </summary>
    public string Album { get; set; } = "";

    /// <summary>
    /// 显示标题。
    /// </summary>
    public string DisplayTitle => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Title} - {Artist}";

    /// <summary>
    /// 来源显示文本。
    /// </summary>
    public string SourceLabel => Source switch
    {
        "wy" => Localization.Get("App.Music.Source.wy"),
        "qq" => Localization.Get("App.Music.Source.qq"),
        _ => Source
    };
    internal void RefreshLocalization()
    {
        OnPropertyChanged(nameof(SourceLabel));
    }
}
