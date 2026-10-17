using Yohuke.DanmuNeo.I18n;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 工作区设置页。
/// </summary>
public partial class WorkspaceSettingsView : UserControl
{
    /// <summary>
    /// 初始化工作区设置页。
    /// </summary>
    public WorkspaceSettingsView()
    {
        InitializeComponent();
    }

    [RelayCommand]
    private void OpenDeleteWorkspaceDialog(WorkspaceViewModel? workspace)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OpenDeleteWorkspaceDialog(workspace);
        }
    }

    [RelayCommand]
    private void OpenDeleteRoomDialog(LiveRoomTabViewModel? room)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OpenDeleteRoomDialog(room);
        }
    }

    [RelayCommand]
    private async Task ClearWorkspaceAccountAsync()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.ClearWorkspaceAccountAsync();
        }
    }

    [RelayCommand]
    private async Task SetWorkspaceAccountAsync(BilibiliAccount? account)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.SetWorkspaceAccountAsync(account);
        }
    }

    private async void RoomInfo_OnLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            sender is not Control { DataContext: LiveRoomTabViewModel room })
        {
            return;
        }

        await viewModel.SaveRoomInfoAsync(room);
    }

    private async void ExportWorkspace_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            TopLevel.GetTopLevel(this) is not TopLevel topLevel)
        {
            return;
        }

        var workspace = viewModel.SelectedWorkspace;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new()
        {
            Title = Localization.Get("UI.WorkspaceSettingsView.ExportWorkspace"),
            SuggestedFileName = viewModel.GetExportFileName(workspace),
            FileTypeChoices =
            [
                new FilePickerFileType(Localization.Get("UI.WorkspaceSettingsView.WorkspaceFileType"))
                {
                    Patterns = ["*.dnworkspace.json"]
                }
            ]
        });

        if (file is null)
        {
            return;
        }

        await viewModel.ExportWorkspaceAsync(workspace, file.Path.LocalPath);
    }
}
