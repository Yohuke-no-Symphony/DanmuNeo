using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 杂项设置视图。
/// </summary>
public partial class MiscSettingsView : UserControl
{
    /// <summary>
    /// 初始化杂项设置视图。
    /// </summary>
    public MiscSettingsView()
    {
        InitializeComponent();
    }

    [RelayCommand]
    private void OpenConfigDirectory()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OpenConfigDirectory();
        }
    }

    [RelayCommand]
    private void OpenDanmuLogsDirectory()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OpenDanmuLogsDirectory();
        }
    }

    [RelayCommand]
    private void OpenCacheDirectory()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OpenCacheDirectory();
        }
    }

    [RelayCommand]
    private void RefreshCacheSize()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.RefreshCacheSize();
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.ClearCacheAsync();
        }
    }

    [RelayCommand]
    private async Task ExportAllBackupAsync()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.ExportAllBackupAsync();
        }
    }
}
