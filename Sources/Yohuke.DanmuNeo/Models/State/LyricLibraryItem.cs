using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Models.State;

/// <summary>
/// 本地歌词库条目。
/// </summary>
public class LyricLibraryItem : ObservableObject
{
    /// <summary>
    /// 歌词条目 ID。
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

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
    /// 歌词来源。
    /// </summary>
    public string Source { get; set; } = "local";

    /// <summary>
    /// 来源歌曲 ID。
    /// </summary>
    public string SourceSongId { get; set; } = "";

    /// <summary>
    /// 检索标签。
    /// </summary>
    public string Tags { get; set; } = "";

    /// <summary>
    /// 是否包含翻译歌词。
    /// </summary>
    public bool HasTranslation { get; set; }

    /// <summary>
    /// 原文歌词。
    /// </summary>
    public string LyricText { get; set; } = "";

    /// <summary>
    /// 翻译歌词。
    /// </summary>
    public string TranslatedLyricText { get; set; } = "";

    /// <summary>
    /// 是否可以在同传中同时显示原文和翻译。
    /// </summary>
    public bool HasMultilingualLyric => !string.IsNullOrWhiteSpace(LyricText) &&
                                        !string.IsNullOrWhiteSpace(TranslatedLyricText);

    /// <summary>
    /// 创建时间。
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// 更新时间。
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// 最后使用时间。
    /// </summary>
    public DateTimeOffset? LastUsedAt { get; set; }

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
        "local" => Localization.Get("App.Music.Source.local"),
        _ => Source
    };
    internal void RefreshLocalization()
    {
        OnPropertyChanged(nameof(SourceLabel));
    }
}
