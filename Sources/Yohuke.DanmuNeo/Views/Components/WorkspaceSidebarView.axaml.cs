using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 工作区侧栏。
/// </summary>
public partial class WorkspaceSidebarView : UserControl
{
    /// <summary>
    /// 初始化工作区侧栏。
    /// </summary>
    public WorkspaceSidebarView()
    {
        InitializeComponent();
    }

    [RelayCommand]
    private void OpenAddWorkspaceDialog()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OpenAddWorkspaceDialog();
        }
    }

    [RelayCommand]
    private void SelectWorkspace(WorkspaceViewModel? workspace)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectWorkspace(workspace);
        }
    }

    [RelayCommand]
    private void SelectRoom(LiveRoomTabViewModel? room)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.SelectRoom(room);
        }
    }

    [RelayCommand]
    private async Task MoveWorkspaceUpAsync(WorkspaceViewModel? workspace)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.MoveWorkspaceUpAsync(workspace);
        }
    }

    [RelayCommand]
    private async Task MoveWorkspaceDownAsync(WorkspaceViewModel? workspace)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.MoveWorkspaceDownAsync(workspace);
        }
    }

    [RelayCommand]
    private async Task MoveRoomUpAsync(LiveRoomTabViewModel? room)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.MoveRoomUpAsync(room);
        }
    }

    [RelayCommand]
    private async Task MoveRoomDownAsync(LiveRoomTabViewModel? room)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.MoveRoomDownAsync(room);
        }
    }

    private async void ExportWorkspace_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            TopLevel.GetTopLevel(this) is not TopLevel topLevel)
        {
            return;
        }

        var workspace = (sender as Control)?.DataContext as WorkspaceViewModel ?? viewModel.SelectedWorkspace;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new()
        {
            Title = "导出工作区",
            SuggestedFileName = viewModel.GetExportFileName(workspace),
            FileTypeChoices =
            [
                new FilePickerFileType("DanmuNeo Workspace")
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

    private async void ImportWorkspace_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            TopLevel.GetTopLevel(this) is not TopLevel topLevel)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "导入工作区",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("DanmuNeo Workspace")
                {
                    Patterns = ["*.dnworkspace.json", "*.json"]
                }
            ]
        });

        var file = files.FirstOrDefault();

        if (file is null)
        {
            return;
        }

        await viewModel.ImportWorkspaceAsync(file.Path.LocalPath);
    }

    private void OpenWorkspaceSettings_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var workspace = (sender as Control)?.DataContext as WorkspaceViewModel;
        viewModel.OpenWorkspaceSettings(workspace);
    }

    private void OpenAddRoomDialog_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var workspace = (sender as Control)?.DataContext as WorkspaceViewModel;
        viewModel.OpenAddRoomDialog(workspace);
    }

    private void WorkspaceRow_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var workspace = (sender as Control)?.DataContext as WorkspaceViewModel;
        viewModel.ToggleWorkspaceExpanded(workspace);
    }

    private void DeleteWorkspace_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var workspace = (sender as Control)?.DataContext as WorkspaceViewModel;
        viewModel.OpenDeleteWorkspaceDialog(workspace);
    }

    private void DeleteRoom_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var room = (sender as Control)?.DataContext as LiveRoomTabViewModel;
        viewModel.OpenDeleteRoomDialog(room);
    }
}
