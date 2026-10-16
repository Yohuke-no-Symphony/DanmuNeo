using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.Views;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 歌词库列表控件。
/// </summary>
public partial class LyricLibraryListView : UserControl
{
    /// <summary>
    /// 是否显示修改操作。
    /// </summary>
    public static readonly StyledProperty<bool> ShowEditButtonProperty =
        AvaloniaProperty.Register<LyricLibraryListView, bool>(nameof(ShowEditButton), true);

    /// <summary>
    /// 是否在控件内部滚动歌词列表。
    /// </summary>
    public static readonly StyledProperty<bool> EnableResultScrollingProperty =
        AvaloniaProperty.Register<LyricLibraryListView, bool>(nameof(EnableResultScrolling));

    /// <summary>
    /// 初始化歌词库列表控件。
    /// </summary>
    public LyricLibraryListView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 是否显示修改操作。
    /// </summary>
    public bool ShowEditButton
    {
        get => GetValue(ShowEditButtonProperty);
        set => SetValue(ShowEditButtonProperty, value);
    }

    /// <summary>
    /// 是否在控件内部滚动歌词列表。
    /// </summary>
    public bool EnableResultScrolling
    {
        get => GetValue(EnableResultScrollingProperty);
        set => SetValue(EnableResultScrollingProperty, value);
    }

    [RelayCommand]
    private async Task PickLyricLibraryItemAsync(LyricLibraryItem? item)
    {
        if (GetMainWindowViewModel() is MainWindowViewModel viewModel)
        {
            await viewModel.PickLyricLibraryItemAsync(item);
        }
    }

    [RelayCommand]
    private async Task DeleteLyricLibraryItemAsync(LyricLibraryItem? item)
    {
        if (GetMainWindowViewModel() is MainWindowViewModel viewModel)
        {
            await viewModel.DeleteLyricLibraryItemAsync(item);
        }
    }

    private void MoreLyric_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: LyricLibraryItem item } button)
        {
            return;
        }

        var menu = new ContextMenu
        {
            ItemsSource = new MenuItem[]
            {
                new()
                {
                    Header = "修改",
                    IsVisible = ShowEditButton,
                    Command = EditLyricCommand,
                    CommandParameter = item
                },
                new()
                {
                    Header = "删除",
                    Command = DeleteLyricLibraryItemCommand,
                    CommandParameter = item
                }
            }
        };
        menu.Open(button);
    }

    [RelayCommand]
    private async Task EditLyricAsync(LyricLibraryItem? item)
    {
        if (item is null ||
            TopLevel.GetTopLevel(this) is not Window owner ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var window = new LyricEditDialogWindow(item);
        var saved = await window.ShowDialog<bool>(owner);

        if (saved)
        {
            await viewModel.SaveLyricLibraryEditAsync();
        }
    }

    private MainWindowViewModel? GetMainWindowViewModel()
    {
        return DataContext as MainWindowViewModel;
    }
}
