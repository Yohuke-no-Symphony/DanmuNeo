using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 独立歌词面板，保留自动滚动、复制和长按微调语义。
/// </summary>
public partial class LyricsPanelView : WorkbenchRoomPanel
{
    private DispatcherTimer? lyricSeekRepeatTimer;
    private Key? lyricSeekRepeatKey;
    private Key? lyricNextShortcutKey;
    private double lyricSeekRepeatOffsetSeconds;
    private bool isLyricSeekRepeating;
    private bool isLyricAutoFollowEnabled = true;
    private WorkbenchSelection? holdSelection;

    /// <summary>
    /// 创建并连接歌词面板。
    /// </summary>
    public LyricsPanelView(WorkbenchContext context) : this()
    {
        InitializeContext(context);
    }

    /// <summary>创建供 XAML 加载器使用的歌词面板。</summary>
    public LyricsPanelView()
    {
        InitializeComponent();
        LyricListBox.AddHandler(PointerPressedEvent, LyricListBox_OnPointerPressed, RoutingStrategies.Tunnel, true);
        LyricListBox.AddHandler(PointerWheelChangedEvent, LyricListBox_OnPointerWheelChanged, RoutingStrategies.Tunnel, true);
        LyricListBox.AddHandler(KeyDownEvent, LyricListBox_OnKeyDown, RoutingStrategies.Tunnel, true);
    }

    /// <summary>
    /// 请求由工作台窗口注册表打开歌词库 owned 窗口。
    /// </summary>
    public event Action<Window?>? LyricLibraryRequested;

    /// <summary>
    /// 开始指定方向的长按时间微调。
    /// </summary>
    public void StartLyricSeek(Key key, double offsetSeconds)
    {
        StartLyricSeekShortcut(key, offsetSeconds);
    }

    /// <summary>
    /// 下一句快捷键在按住期间只执行一次。
    /// </summary>
    public void SeekNextLyric(Key key)
    {
        if (lyricNextShortcutKey != key)
        {
            lyricNextShortcutKey = key;
            SeekNextLyricFromShortcut();
        }
    }

    /// <summary>
    /// 释放已按下的歌词快捷键。
    /// </summary>
    public void ReleaseKey(Key key)
    {
        if (lyricNextShortcutKey == key)
        {
            lyricNextShortcutKey = null;
        }

        if (lyricSeekRepeatKey == key)
        {
            StopLyricSeekRepeat();
        }
    }

    /// <summary>
    /// 失焦、模态、切房和宿主关闭时停止长按动作。
    /// </summary>
    public void StopHeldActions()
    {
        lyricNextShortcutKey = null;
        holdSelection = null;
        StopLyricSeekRepeat();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopHeldActions();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc/>
    protected override void OnCurrentRoomChanged(LiveRoomTabViewModel? oldRoom, LiveRoomTabViewModel? newRoom)
    {
        StopHeldActions();
        isLyricAutoFollowEnabled = true;

        if (oldRoom is not null)
        {
            oldRoom.PropertyChanged -= Room_OnPropertyChanged;
        }

        if (newRoom is not null)
        {
            newRoom.PropertyChanged += Room_OnPropertyChanged;
        }
    }

    /// <inheritdoc/>
    protected override void DisposePanel()
    {
        StopHeldActions();

        if (lyricSeekRepeatTimer is not null)
        {
            lyricSeekRepeatTimer.Tick -= LyricSeekRepeatTimer_OnTick;
        }
    }

    [RelayCommand]
    private void OpenLyricLibraryPicker()
    {
        LyricLibraryRequested?.Invoke(TopLevel.GetTopLevel(this) as Window);
    }

    private void Room_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LiveRoomTabViewModel.IsLyricAutoSending) &&
            Context.CurrentRoom?.IsLyricAutoSending == true)
        {
            isLyricAutoFollowEnabled = true;
        }

        if (isLyricAutoFollowEnabled && e.PropertyName is nameof(LiveRoomTabViewModel.ActiveLyricLine) or
            nameof(LiveRoomTabViewModel.IsLyricAutoSending))
        {
            var selection = Context.CaptureSelection();
            Dispatcher.UIThread.Post(() =>
            {
                if (Context.IsCurrent(selection))
                {
                    ScrollActiveLyricIntoView();
                }
            }, DispatcherPriority.Background);
        }
    }

    private async Task CopyAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private async void LyricTitle_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is LiveRoomTabViewModel { HasLyricTitle: true } room)
        {
            await CopyAsync(room.LyricTitle);
        }
    }

    private void NextLyricButton_OnClick(object? sender, RoutedEventArgs e)
    {
        isLyricAutoFollowEnabled = true;
    }

    /// <summary>
    /// 从快捷键跳到下一句并发送，同时恢复歌词列表跟随。
    /// </summary>
    public void SeekNextLyricFromShortcut()
    {
        if (Context.CurrentRoom is not { } room || !room.SeekNextLyricCommand.CanExecute(null))
        {
            return;
        }

        isLyricAutoFollowEnabled = true;
        room.SeekNextLyricCommand.Execute(null);
    }

    private async void LyricLine_OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not LyricLineState line ||
            DataContext is not LiveRoomTabViewModel room)
        {
            return;
        }

        isLyricAutoFollowEnabled = true;
        e.Handled = true;
        await room.SeekAndSendLyricLineAsync(line);
    }

    private void LyricListBox_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        SuspendLyricAutoFollow();
    }

    private void LyricListBox_OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        SuspendLyricAutoFollow();
    }

    private async void LyricListBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        var application = Context.Application;

        if (e.Handled || application.IsRecordingShortcut || application.IsSettingsOpen || application.IsWorkspaceSettingsOpen ||
            application.IsAddDialogOpen || application.IsConfirmDeleteDialogOpen ||
            WorkbenchTextInput.IsComposing(e.Source))
        {
            return;
        }

        if (ShortcutBindingService.GetTriggeredActionKey(application.Settings.ShortcutBindings,
                e.Key, e.KeyModifiers) == ShortcutActionKeys.LYRIC_COPY_SELECTED &&
            DataContext is LiveRoomTabViewModel room &&
            LyricListBox.SelectedItem is LyricLineState line)
        {
            e.Handled = true;
            var text = LyricTimelineService.CreateContent(line.Content, line.TranslatedContent, room.LyricSendMode);

            if (!string.IsNullOrWhiteSpace(text))
            {
                await CopyAsync(text);
            }

            return;
        }

        if (e.Key is Key.PageUp or Key.PageDown or Key.Home or Key.End or Key.Up or Key.Down)
        {
            SuspendLyricAutoFollow();
        }
    }

    private void SuspendLyricAutoFollow()
    {
        if (Context.CurrentRoom?.IsLyricAutoSending == true)
        {
            isLyricAutoFollowEnabled = false;
        }
    }

    private void StartLyricSeekShortcut(Key key, double offsetSeconds)
    {
        if (lyricSeekRepeatTimer?.IsEnabled == true && lyricSeekRepeatKey == key)
        {
            return;
        }

        Context.CurrentRoom?.AdjustLyricPlaybackPosition(offsetSeconds);
        StartLyricSeekRepeat(key, offsetSeconds);
    }

    private void StartLyricSeekRepeat(Key key, double offsetSeconds)
    {
        holdSelection = Context.CaptureSelection();
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
        if (!Context.IsCurrent(holdSelection))
        {
            StopHeldActions();
            return;
        }

        if (!isLyricSeekRepeating)
        {
            isLyricSeekRepeating = true;
            lyricSeekRepeatTimer!.Interval = TimeSpan.FromMilliseconds(250);
        }

        Context.CurrentRoom?.AdjustLyricPlaybackPosition(lyricSeekRepeatOffsetSeconds);
    }

    private void ScrollActiveLyricIntoView()
    {
        if (!isLyricAutoFollowEnabled || Context.CurrentRoom?.ActiveLyricLine is null)
        {
            return;
        }

        LyricListBox.ScrollIntoView(Context.CurrentRoom.ActiveLyricLine);
    }

}
