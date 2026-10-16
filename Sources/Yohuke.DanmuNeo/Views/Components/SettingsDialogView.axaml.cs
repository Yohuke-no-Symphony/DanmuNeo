using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 设置对话框。
/// </summary>
public partial class SettingsDialogView : UserControl
{
    /// <summary>
    /// 初始化设置对话框。
    /// </summary>
    public SettingsDialogView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 请求宿主使用统一的保存和关闭入口。
    /// </summary>
    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void CloseSettings()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SelectSettingsSection(SettingsSectionOption? option)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectSettingsSection(option);
        }
    }
}
