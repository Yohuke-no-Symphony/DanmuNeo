using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 独立工作区设置窗口的内容与保存请求。
/// </summary>
public partial class WorkspaceSettingsDialogView : UserControl
{
    /// <summary>
    /// 初始化独立工作区设置内容。
    /// </summary>
    public WorkspaceSettingsDialogView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// 请求窗口宿主保存并关闭。
    /// </summary>
    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void CloseWorkspaceSettings()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
