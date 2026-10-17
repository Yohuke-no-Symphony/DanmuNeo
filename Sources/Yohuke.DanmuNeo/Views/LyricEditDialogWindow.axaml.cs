using Yohuke.DanmuNeo.I18n;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views;

/// <summary>
/// 歌词编辑弹窗。
/// </summary>
public partial class LyricEditDialogWindow : Window
{
    private readonly LyricLibraryItem item;

    /// <summary>
    /// 初始化歌词编辑弹窗。
    /// </summary>
    public LyricEditDialogWindow()
        : this(new())
    {
    }

    /// <summary>
    /// 初始化歌词编辑弹窗。
    /// </summary>
    public LyricEditDialogWindow(LyricLibraryItem item)
    {
        this.item = item;
        InitializeComponent();
        WindowChrome.AttachFrame(this);
        DataContext = new LyricEditDialogViewModel(item);
        AddHandler(KeyDownEvent, LyricEditDialogWindow_OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Opened += LyricEditDialogWindow_OnOpened;
        Closed += (_, _) => (DataContext as LyricEditDialogViewModel)?.Dispose();
    }

    private void LyricEditDialogWindow_OnOpened(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Background);
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void Save_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LyricEditDialogViewModel viewModel)
        {
            SyncLargeTextBoxes(viewModel);
            var candidate = CreateCandidate();
            viewModel.ApplyTo(candidate);

            try
            {
                new LyricLibraryService().Validate(candidate);
            }
            catch (Exception exception)
            {
                viewModel.SetLocalizedError("UI.LyricEditDialogWindow.ValidationFailed",
                    new Func<string>(() => LocalizedExceptionText.Get(exception)));
                StartupLog.Append(Localization.Get("UI.LyricEditDialogWindow.ValidationLogFailed"), Models.Logging.AppLogLevel.WARNING, Localization.Get("UI.LyricEditDialogWindow.LogSource"), exception);
                return;
            }

            viewModel.ApplyTo(item);
        }

        Close(true);
    }

    private LyricLibraryItem CreateCandidate()
    {
        return new()
        {
            Id = item.Id,
            Source = item.Source,
            SourceSongId = item.SourceSongId,
            CreatedAt = item.CreatedAt,
            LastUsedAt = item.LastUsedAt
        };
    }

    private void LyricEditDialogWindow_OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (!OperatingSystem.IsMacOS() || !IsMacSpecialInputKey(e.Key) || !IsLargeLyricTextBoxEvent(e))
        {
            return;
        }

        StartupLog.Append(Localization.Format("UI.LyricEditDialogWindow.IgnoredSpecialKey", e.Key));
        e.Handled = true;
    }

    private static bool IsMacSpecialInputKey(Key key)
    {
        return key is Key.None or Key.ImeProcessed or Key.DeadCharProcessed;
    }

    private bool IsLargeLyricTextBoxEvent(KeyEventArgs e)
    {
        if (e.Source is not Control control)
        {
            return false;
        }

        var textBox = control as TextBox ?? control.FindAncestorOfType<TextBox>();
        return textBox == OriginalLyricTextBox || textBox == TranslatedLyricTextBox;
    }

    private void SyncLargeTextBoxes(LyricEditDialogViewModel viewModel)
    {
        viewModel.LyricText = OriginalLyricTextBox.Text ?? "";
        viewModel.TranslatedLyricText = TranslatedLyricTextBox.Text ?? "";
    }
}
