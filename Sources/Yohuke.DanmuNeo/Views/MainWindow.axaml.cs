using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Views;

/// <summary>
/// 主窗口。
/// </summary>
public partial class MainWindow : Window
{
    private readonly AttachedDialogCoordinator dialogCoordinator;
    private readonly double expandedSidebarMinWidth;
    private readonly GridLength expandedSidebarSplitterWidth;
    private MainWindowViewModel? sidebarViewModel;
    private DispatcherTimer? lyricSeekRepeatTimer;
    private Key? lyricSeekRepeatKey;
    private Key? lyricNextShortcutKey;
    private double lyricSeekRepeatOffsetSeconds;
    private bool isLyricSeekRepeating;

    /// <summary>
    /// 初始化主窗口。
    /// </summary>
    public MainWindow()
    {
        StartupLog.Append("MainWindow ctor begin");
        InitializeComponent();
        expandedSidebarMinWidth = GetSidebarColumn().MinWidth;
        expandedSidebarSplitterWidth = MainColumns.ColumnDefinitions[1].Width;
        StartupLog.Append("MainWindow InitializeComponent end");
        dialogCoordinator = new(this, MainColumns);
        DataContextChanged += OnDataContextChanged;
        AddHandler(KeyDownEvent, MainWindow_OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, MainWindow_OnPreviewKeyUp, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => lyricNextShortcutKey = null;
        Closing += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                SaveWindowSize(viewModel);

                if (!viewModel.IsSidebarCollapsed)
                {
                    CaptureSidebarWidth(viewModel);
                }

                await viewModel.SaveAsync();
            }
        };
        Closed += (_, _) =>
        {
            AttachSidebarViewModel(null);
            dialogCoordinator.Dispose();

            if (DataContext is IDisposable disposable)
            {
                disposable.Dispose();
            }
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        lyricNextShortcutKey = null;
        AttachSidebarViewModel(DataContext as MainWindowViewModel);
        dialogCoordinator.Attach(DataContext as MainWindowViewModel);

        if (DataContext is MainWindowViewModel viewModel)
        {
            Width = AppStateService.ClampWindowWidth(viewModel.Settings.WindowWidth);
            Height = AppStateService.ClampWindowHeight(viewModel.Settings.WindowHeight);
            UpdateSidebarLayout(viewModel);
        }
    }

    private void AttachSidebarViewModel(MainWindowViewModel? viewModel)
    {
        if (sidebarViewModel is not null)
        {
            sidebarViewModel.PropertyChanged -= SidebarViewModel_OnPropertyChanged;
        }

        sidebarViewModel = viewModel;

        if (sidebarViewModel is not null)
        {
            sidebarViewModel.PropertyChanged += SidebarViewModel_OnPropertyChanged;
        }
    }

    private void SidebarViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsSidebarCollapsed) ||
            sender is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsSidebarCollapsed)
        {
            CaptureSidebarWidth(viewModel);
        }

        UpdateSidebarLayout(viewModel);
    }

    private void UpdateSidebarLayout(MainWindowViewModel viewModel)
    {
        var sidebarColumn = GetSidebarColumn();
        sidebarColumn.MinWidth = viewModel.IsSidebarCollapsed ? 0 : expandedSidebarMinWidth;
        sidebarColumn.Width = new(viewModel.IsSidebarCollapsed
            ? 0
            : AppStateService.ClampSidebarWidth(viewModel.Settings.SidebarWidth));
        MainColumns.ColumnDefinitions[1].Width = viewModel.IsSidebarCollapsed ? new(0) : expandedSidebarSplitterWidth;
    }

    private void CaptureSidebarWidth(MainWindowViewModel viewModel)
    {
        var sidebarColumn = GetSidebarColumn();
        var width = sidebarColumn.ActualWidth > 0 ? sidebarColumn.ActualWidth : sidebarColumn.Width.Value;
        viewModel.Settings.SidebarWidth = AppStateService.ClampSidebarWidth(width);
    }

    private void MainWindow_OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (!TryHandleShortcut(e))
        {
            return;
        }

        e.Handled = true;
    }

    private void MainWindow_OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (lyricNextShortcutKey == e.Key)
        {
            lyricNextShortcutKey = null;
        }

        if (lyricSeekRepeatKey == e.Key)
        {
            StopLyricSeekRepeat();
        }
    }

    private async void SidebarSplitter_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is MainWindowViewModel { IsSidebarCollapsed: false } viewModel)
        {
            CaptureSidebarWidth(viewModel);
            GetSidebarColumn().Width = new(viewModel.Settings.SidebarWidth);
            await viewModel.SaveAsync();
        }
    }

    private bool TryHandleShortcut(KeyEventArgs e)
    {
        if (e.Handled || DataContext is not MainWindowViewModel viewModel || viewModel.IsRecordingShortcut ||
            viewModel.IsSettingsOpen || viewModel.IsAddDialogOpen || viewModel.IsConfirmDeleteDialogOpen)
        {
            return false;
        }

        var actionKey = ShortcutBindingService.GetTriggeredActionKey(
            viewModel.Settings.ShortcutBindings,
            e.Key,
            e.KeyModifiers);

        if (actionKey is null || IsTextInputEvent(e) &&
            actionKey is not (ShortcutActionKeys.INPUT_CLEAR_DRAFT or ShortcutActionKeys.INPUT_CYCLE_MARK_GROUP))
        {
            return false;
        }

        if (actionKey == ShortcutActionKeys.INPUT_CYCLE_MARK_GROUP &&
            viewModel.SelectedWorkspace is not null && viewModel.SymbolGroups.Count > 0)
        {
            viewModel.CycleWorkspaceMarkGroupCommand.Execute(null);
            return true;
        }

        return viewModel.SelectedWorkspace?.SelectedRoom is { } room && ExecuteShortcut(room, actionKey, e.Key);
    }

    private bool ExecuteShortcut(LiveRoomTabViewModel room, string actionKey, Key key)
    {
        switch (actionKey)
        {
            case ShortcutActionKeys.LIVE_ROOM_START_LISTENING:
                room.StartListening();
                return true;
            case ShortcutActionKeys.LIVE_ROOM_STOP_LISTENING:
                room.StopListening();
                return true;
            case ShortcutActionKeys.LIVE_PLAYER_START:
                _ = room.PlayLiveAsync();
                return true;
            case ShortcutActionKeys.LIVE_PLAYER_STOP:
                room.StopLivePlayer();
                return true;
            case ShortcutActionKeys.LIVE_PLAYER_CHASE:
                _ = room.ChaseLiveAsync();
                return true;
            case ShortcutActionKeys.INPUT_FOCUS_DRAFT:
                GetActiveLiveRoomView()?.FocusInputDraftFromShortcut();
                return true;
            case ShortcutActionKeys.INPUT_CLEAR_DRAFT:
                ClearInputDraft(room);
                return true;
            case ShortcutActionKeys.LYRIC_START_SENDING:
                _ = room.StartAutoLyricAsync();
                return true;
            case ShortcutActionKeys.LYRIC_STOP_SENDING:
                room.StopAutoLyric();
                return true;
            case ShortcutActionKeys.LYRIC_SEEK_BACKWARD:
                StartLyricSeekShortcut(room, key, -0.5);
                return true;
            case ShortcutActionKeys.LYRIC_SEEK_FORWARD:
                StartLyricSeekShortcut(room, key, 0.5);
                return true;
            case ShortcutActionKeys.LYRIC_SEEK_NEXT:
                if (lyricNextShortcutKey != key)
                {
                    lyricNextShortcutKey = key;
                    var view = GetActiveLiveRoomView();

                    if (view is not null)
                    {
                        view.SeekNextLyricFromShortcut();
                    }
                    else if (room.SeekNextLyricCommand.CanExecute(null))
                    {
                        room.SeekNextLyricCommand.Execute(null);
                    }
                }

                return true;
            default:
                return false;
        }
    }

    private void ClearInputDraft(LiveRoomTabViewModel room)
    {
        var view = GetActiveLiveRoomView();

        if (view is not null)
        {
            view.ClearInputDraftFromShortcut();
            return;
        }

        room.ClearInputDraft();
    }

    private LiveRoomWorkspaceView? GetActiveLiveRoomView()
    {
        return this.GetVisualDescendants().OfType<LiveRoomWorkspaceView>().FirstOrDefault();
    }

    private static bool IsTextInputEvent(KeyEventArgs e)
    {
        if (e.Source is TextBox)
        {
            return true;
        }

        return e.Source is Control control && control.FindAncestorOfType<TextBox>() is not null;
    }

    private void StartLyricSeekShortcut(LiveRoomTabViewModel room, Key key, double offsetSeconds)
    {
        if (lyricSeekRepeatTimer?.IsEnabled == true && lyricSeekRepeatKey == key)
        {
            return;
        }

        room.AdjustLyricPlaybackPosition(offsetSeconds);
        lyricSeekRepeatKey = key;
        lyricSeekRepeatOffsetSeconds = offsetSeconds;
        isLyricSeekRepeating = false;
        lyricSeekRepeatTimer ??= new();
        lyricSeekRepeatTimer.Stop();
        lyricSeekRepeatTimer.Interval = TimeSpan.FromSeconds(1);
        lyricSeekRepeatTimer.Tick -= LyricSeekRepeatTimer_OnTick;
        lyricSeekRepeatTimer.Tick += LyricSeekRepeatTimer_OnTick;
        lyricSeekRepeatTimer.Start();
    }

    private void StopLyricSeekRepeat()
    {
        lyricSeekRepeatTimer?.Stop();
        lyricSeekRepeatKey = null;
        lyricSeekRepeatOffsetSeconds = 0;
        isLyricSeekRepeating = false;
    }

    private void LyricSeekRepeatTimer_OnTick(object? sender, EventArgs e)
    {
        if (!isLyricSeekRepeating)
        {
            isLyricSeekRepeating = true;
            lyricSeekRepeatTimer!.Interval = TimeSpan.FromMilliseconds(250);
        }

        if (DataContext is MainWindowViewModel viewModel &&
            viewModel.SelectedWorkspace?.SelectedRoom is { } room)
        {
            room.AdjustLyricPlaybackPosition(lyricSeekRepeatOffsetSeconds);
        }
    }

    private ColumnDefinition GetSidebarColumn()
    {
        return MainColumns.ColumnDefinitions[0];
    }

    private void SaveWindowSize(MainWindowViewModel viewModel)
    {
        if (WindowState != WindowState.Normal)
        {
            return;
        }

        viewModel.Settings.WindowWidth = AppStateService.ClampWindowWidth(Width);
        viewModel.Settings.WindowHeight = AppStateService.ClampWindowHeight(Height);
    }
}
