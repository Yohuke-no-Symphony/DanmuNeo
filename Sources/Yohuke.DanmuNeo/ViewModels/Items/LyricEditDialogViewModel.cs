using CommunityToolkit.Mvvm.ComponentModel;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.ViewModels.Items;

/// <summary>
/// 歌词编辑弹窗视图模型。
/// </summary>
public class LyricEditDialogViewModel : ObservableObject, IDisposable
{
    private string errorText = "";
    private LocalizedMessage? localizedError;

    /// <summary>
    /// 初始化歌词编辑弹窗视图模型。
    /// </summary>
    public LyricEditDialogViewModel(LyricLibraryItem item)
    {
        Localization.LanguageChanged += OnLanguageChanged;
        Title = item.Title;
        Artist = item.Artist;
        Album = item.Album;
        Tags = item.Tags;
        LyricText = item.LyricText;
        TranslatedLyricText = item.TranslatedLyricText;
    }

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
    /// 标签。
    /// </summary>
    public string Tags { get; set; } = "";

    /// <summary>
    /// 原文歌词。
    /// </summary>
    public string LyricText { get; set; } = "";

    /// <summary>
    /// 翻译歌词。
    /// </summary>
    public string TranslatedLyricText { get; set; } = "";

    /// <summary>
    /// 错误提示。
    /// </summary>
    public string ErrorText
    {
        get => errorText;
        set
        {
            localizedError = null;

            if (SetProperty(ref errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    /// <summary>
    /// 是否存在错误。
    /// </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    /// <summary>
    /// 记录可随界面语言变化刷新的错误。
    /// </summary>
    public void SetLocalizedError(string key, params object[] arguments)
    {
        var message = new LocalizedMessage(key, arguments);
        ErrorText = message.Render();
        localizedError = message;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Localization.LanguageChanged -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (localizedError is { } message)
        {
            ErrorText = message.Render();
            localizedError = message;
        }
    }

    /// <summary>
    /// 写回歌词库条目。
    /// </summary>
    public void ApplyTo(LyricLibraryItem item)
    {
        item.Title = Title.Trim();
        item.Artist = Artist.Trim();
        item.Album = Album.Trim();
        item.Tags = Tags.Trim();
        item.LyricText = LyricText;
        item.TranslatedLyricText = TranslatedLyricText;
        item.HasTranslation = !string.IsNullOrWhiteSpace(TranslatedLyricText);
        item.UpdatedAt = DateTimeOffset.Now;
    }
}
