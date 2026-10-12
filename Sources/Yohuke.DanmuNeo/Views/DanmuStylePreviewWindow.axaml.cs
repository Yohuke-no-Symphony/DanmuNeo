using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Views;

/// <summary>
/// 提供普通弹幕、粉丝牌、SC 和上舰记录的离线样式与滚动行为预览。
/// </summary>
public partial class DanmuStylePreviewWindow : Window
{
    private readonly DanmuStylePreviewViewModel viewModel = new();
    private readonly DanmuFeedScrollController scrollController;

    /// <summary>
    /// 初始化离线预览样本并将弹幕列表接入与正式房间相同的滚动控制器。
    /// </summary>
    public DanmuStylePreviewWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
        scrollController = new(
            this.FindControl<ScrollViewer>("DanmuScrollViewer")!,
            this.FindControl<ItemsControl>("DanmuItemsControl")!);
        scrollController.Attach(viewModel.DanmuItems);
        Closed += (_, _) => scrollController.Dispose();
        Opened += (_, _) =>
        {
            var ownerViewModel = Owner?.DataContext as MainWindowViewModel;
            CurrentMarkGroupPanel.IsVisible = ownerViewModel is not null;
            CurrentMarkGroupText.DataContext = ownerViewModel;
        };
        AddHandler(KeyDownEvent, PreviewWindow_OnPreviewKeyDown, RoutingStrategies.Tunnel);
        ThemeModeComboBox.SelectedIndex = 0;
    }

    private void PreviewWindow_OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || Owner?.DataContext is not MainWindowViewModel ownerViewModel ||
            ownerViewModel.IsRecordingShortcut || ownerViewModel.IsSettingsOpen || ownerViewModel.IsAddDialogOpen ||
            ownerViewModel.IsConfirmDeleteDialogOpen || ownerViewModel.SelectedWorkspace is null ||
            ownerViewModel.SymbolGroups.Count == 0)
        {
            return;
        }

        var actionKey = ShortcutBindingService.GetTriggeredActionKey(
            ownerViewModel.Settings.ShortcutBindings, e.Key, e.KeyModifiers);

        if (actionKey == ShortcutActionKeys.INPUT_CYCLE_MARK_GROUP)
        {
            ownerViewModel.CycleWorkspaceMarkGroupCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ThemeMode_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        RequestedThemeVariant = comboBox.SelectedIndex switch
        {
            1 => ThemeVariant.Light,
            2 => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    private void AddDanmu_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        viewModel.AddDanmu();
    }

    private void AddDanmuBatch_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        viewModel.AddDanmuBatch();
    }

    private void AddSuperChat_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        viewModel.AddSuperChat();
    }

    private async void FeedUserName_OnClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Button { Content: TextBlock { Text: { } userName } } && Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(userName);
        }
    }

    private async void SuperChat_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: SuperChatItem { IsGuardPurchase: true } item } &&
            Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(item.UserName);
        }
    }

    private void AddGuardPurchase_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        viewModel.AddGuardPurchase();
    }

    private void ResetSamples_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        viewModel.ResetSamples();
    }

    private void ClearSamples_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        viewModel.ClearSamples();
    }
}
