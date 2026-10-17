using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 展示会话日志，负责剪贴板和可见窗口的已读边界。
/// </summary>
public sealed partial class LogPanelView : UserControl, IDisposable
{
    private readonly LogPanelViewModel? viewModel;
    private readonly IAppLogService? log;
    private Window? observedWindow;
    private bool disposed;

    /// <summary>创建独立于工作区和直播间选择的日志面板。</summary>
    public LogPanelView(IAppLogService log) : this()
    {
        this.log = log;
        viewModel = new(log);
        DataContext = viewModel;
        IsEnabled = true;
        viewModel.SetReadingState(IsReading);
        AttachedToVisualTree += OnAttached;
        DetachedFromVisualTree += OnDetached;
        LayoutUpdated += OnLayoutUpdated;
    }

    /// <summary>创建供 XAML 加载器使用的无数据面板，不创建会话日志。</summary>
    public LogPanelView()
    {
        AvaloniaXamlLoader.Load(this);
        IsEnabled = false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ObserveWindow(null);
        AttachedToVisualTree -= OnAttached;
        DetachedFromVisualTree -= OnDetached;
        LayoutUpdated -= OnLayoutUpdated;
        viewModel?.Dispose();
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ObserveWindow(TopLevel.GetTopLevel(this) as Window);
        RefreshReading();
    }

    private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ObserveWindow(null);
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        RefreshReading();
    }

    private void ObserveWindow(Window? window)
    {
        if (ReferenceEquals(observedWindow, window))
        {
            return;
        }

        if (observedWindow is not null)
        {
            observedWindow.Activated -= OnWindowVisibilityChanged;
            observedWindow.PropertyChanged -= OnWindowPropertyChanged;
        }

        observedWindow = window;

        if (observedWindow is not null)
        {
            observedWindow.Activated += OnWindowVisibilityChanged;
            observedWindow.PropertyChanged += OnWindowPropertyChanged;
        }
    }

    private void OnWindowVisibilityChanged(object? sender, EventArgs e)
    {
        RefreshReading();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == IsVisibleProperty)
        {
            RefreshReading();
        }
    }

    private void RefreshReading()
    {
        viewModel?.MarkVisibleEntriesRead();
    }

    private bool IsReading()
    {
        return !disposed && this.IsAttachedToVisualTree() && IsEffectivelyVisible &&
               Bounds.Width > 0 && Bounds.Height > 0 &&
               observedWindow is { IsVisible: true, IsActive: true, WindowState: not WindowState.Minimized };
    }

    private async void CopySelected_OnClick(object? sender, RoutedEventArgs e)
    {
        if (viewModel?.SelectedEntry is { } entry)
        {
            await CopyAsync(entry.FullText);
        }
    }

    private async void CopyVisible_OnClick(object? sender, RoutedEventArgs e)
    {
        if (viewModel is not null)
        {
            await CopyAsync(viewModel.CopyVisibleText());
        }
    }

    private async Task CopyAsync(string text)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        }
        catch (Exception error)
        {
            log?.Error("日志", "复制日志失败。", error);
        }
    }
}
