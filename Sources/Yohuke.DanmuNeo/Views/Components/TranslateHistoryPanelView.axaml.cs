using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 独立同传历史和 Excel 导出面板。
/// </summary>
public partial class TranslateHistoryPanelView : WorkbenchRoomPanel
{
    /// <summary>
    /// 创建并连接历史面板。
    /// </summary>
    public TranslateHistoryPanelView(WorkbenchContext context) : this()
    {
        InitializeContext(context);
    }

    /// <summary>
    /// 创建供 XAML 加载器使用的面板实例，运行时通过带上下文的构造入口连接。
    /// </summary>
    public TranslateHistoryPanelView()
    {
        InitializeComponent();
    }

    private async void ExportTranslateHistory_OnClick(object? sender, RoutedEventArgs e)
    {
        if (Context.CurrentRoom is not { HasTranslateHistory: true } room ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var invalidChars = Path.GetInvalidFileNameChars().ToHashSet();
        var header = new string(room.Header.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
        var safeHeader = string.IsNullOrWhiteSpace(header) ? "live-room" : header;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new()
        {
            Title = "导出同传历史",
            SuggestedFileName = $"translate-history-{safeHeader}-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx",
            FileTypeChoices =
            [
                new FilePickerFileType("Excel Workbook")
                {
                    Patterns = ["*.xlsx"],
                    MimeTypes = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"]
                }
            ]
        });

        if (file is not null)
        {
            var path = file.Path.LocalPath;
            await room.ExportTranslateHistoryToFileAsync(string.IsNullOrWhiteSpace(Path.GetExtension(path))
                ? $"{path}.xlsx" : path);
        }
    }
}
