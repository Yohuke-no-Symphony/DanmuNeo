using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>永久图库的界面和文件选择器边界，不依赖当前直播间的数据上下文。</summary>
public partial class LiveCapturesPanelView : WorkbenchPanelView
{
    private readonly CancellationTokenSource panelLifetime = new();
    private readonly CancellationToken panelCancellation;

    /// <summary>创建供 XAML 加载器使用的设计时面板。</summary>
    public LiveCapturesPanelView()
    {
        panelCancellation = panelLifetime.Token;
        InitializeComponent();
    }

    /// <summary>连接工作台和由组合根持有的全局图库。</summary>
    public LiveCapturesPanelView(WorkbenchContext context, LiveCapturesViewModel viewModel) : this()
    {
        DataContext = viewModel;
        InitializeContext(context);
    }

    /// <summary>请求与玩家按钮、原生网页快捷键相同的应用截图动作。</summary>
    public event EventHandler? CaptureRequested;

    /// <inheritdoc/>
    protected override void DisposePanel()
    {
        panelLifetime.Cancel();
        panelLifetime.Dispose();
    }

    private void Capture_OnClick(object? sender, RoutedEventArgs e)
    {
        CaptureRequested?.Invoke(this, EventArgs.Empty);
    }

    private void CaptureList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not LiveCapturesViewModel viewModel)
        {
            return;
        }
        var selected = CaptureList.SelectedItems?.OfType<LiveCaptureItemViewModel>().ToArray() ?? [];
        viewModel.SelectedCaptures.Clear();
        foreach (var item in selected)
        {
            viewModel.SelectedCaptures.Add(item);
        }
    }

    private async void ExportSingle_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LiveCapturesViewModel { SelectedCapture: { } capture } viewModel ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }
        try
        {
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new()
            {
                Title = Localization.Get("Captures.ExportSingle"),
                SuggestedFileName = $"capture-{capture.CapturedAt:yyyyMMdd-HHmmss}.png",
                DefaultExtension = "png",
                FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"], MimeTypes = ["image/png"] }]
            });
            if (file?.TryGetLocalPath() is { } path)
            {
                panelCancellation.ThrowIfCancellationRequested();
                await viewModel.ExportToFileAsync(capture, path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? path : path + ".png", panelCancellation);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            viewModel.ReportExportFailure(exception.Message);
        }
    }

    private async void ExportSelected_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LiveCapturesViewModel viewModel)
        {
            await ExportManyAsync(viewModel.SelectedCaptures.ToArray());
        }
    }

    private async void ExportFiltered_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LiveCapturesViewModel viewModel)
        {
            await ExportManyAsync(viewModel.FilteredCaptures.ToArray());
        }
    }

    private async Task ExportManyAsync(LiveCaptureItemViewModel[] captures)
    {
        if (captures.Length == 0 || DataContext is not LiveCapturesViewModel viewModel ||
            TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }
        try
        {
            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new()
            {
                Title = Localization.Get("Captures.ExportFolder"),
                AllowMultiple = false
            });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is { } directory)
            {
                panelCancellation.ThrowIfCancellationRequested();
                await viewModel.ExportAsync(captures, directory, panelCancellation);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            viewModel.ReportExportFailure(exception.Message);
        }
    }
}
