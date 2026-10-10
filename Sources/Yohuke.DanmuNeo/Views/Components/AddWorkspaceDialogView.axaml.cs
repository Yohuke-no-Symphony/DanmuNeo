using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 新建工作区或直播间对话框。
/// </summary>
public partial class AddWorkspaceDialogView : UserControl
{
    /// <summary>
    /// 初始化新增对话框。
    /// </summary>
    public AddWorkspaceDialogView()
    {
        InitializeComponent();
    }

    private async void LiveRoomSearch_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not MainWindowViewModel viewModel ||
            !viewModel.SearchLiveRoomsCommand.CanExecute(null))
        {
            return;
        }

        e.Handled = true;
        await viewModel.SearchLiveRoomsCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void CloseAddDialog()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CloseAddDialog();
        }
    }

    [RelayCommand]
    private async Task ConfirmAddDialogAsync()
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.ConfirmAddDialogAsync();
        }
    }
}
