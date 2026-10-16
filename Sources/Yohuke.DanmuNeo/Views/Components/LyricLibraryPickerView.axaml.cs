using System.ComponentModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 供独立 owned 窗口承载的歌词库选择器。
/// </summary>
public partial class LyricLibraryPickerView : UserControl, IDisposable
{
    private readonly WorkbenchContext context = null!;
    private bool isDisposed;

    /// <summary>
    /// 为当前工作台创建歌词库选择器。
    /// </summary>
    public LyricLibraryPickerView(WorkbenchContext context) : this()
    {
        this.context = context;
        DataContext = context.Application;
        context.Application.PropertyChanged += Application_OnPropertyChanged;
        context.SelectionChanged += Context_OnSelectionChanged;
    }

    /// <summary>创建供 XAML 加载器使用的选择器。</summary>
    public LyricLibraryPickerView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 选择完成、页面关闭或切房后，请求宿主走统一关闭入口。
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed || context is null)
        {
            return;
        }

        isDisposed = true;
        context.Application.PropertyChanged -= Application_OnPropertyChanged;
        context.SelectionChanged -= Context_OnSelectionChanged;
        context.Application.CloseLyricLibraryPicker();
    }

    [RelayCommand]
    private void Close()
    {
        if (context.Application.IsLyricLibraryPickerOpen)
        {
            context.Application.CloseLyricLibraryPicker();
        }
        else
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Application_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsLyricLibraryPickerOpen) &&
            !context.Application.IsLyricLibraryPickerOpen)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Context_OnSelectionChanged(object? sender, EventArgs e)
    {
        Close();
    }
}
