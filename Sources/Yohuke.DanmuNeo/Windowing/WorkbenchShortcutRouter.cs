using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Windowing;

internal sealed class WorkbenchShortcutRouter : IDisposable
{
    private readonly WorkbenchContext context;
    private readonly WorkbenchPanelSet panels;
    private readonly Action<string> showPanel;
    private readonly Action openSettings;
    private readonly Func<bool> isModal;
    private readonly HashSet<Window> windows = [];
    private readonly HashSet<Key> captureKeys = [];

    internal WorkbenchShortcutRouter(WorkbenchContext context, WorkbenchPanelSet panels,
        Action<string> showPanel, Action openSettings, Func<bool> isModal)
    {
        this.context = context;
        this.panels = panels;
        this.showPanel = showPanel;
        this.openSettings = openSettings;
        this.isModal = isModal;
        context.SelectionChanged += OnSelectionChanged;
    }

    internal void Attach(Window window)
    {
        if (!windows.Add(window))
        {
            return;
        }

        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.LostFocusEvent, OnLostFocus, RoutingStrategies.Bubble);
        window.Deactivated += OnDeactivated;
        window.Closed += OnClosed;
    }

    internal void Detach(Window window)
    {
        if (!windows.Remove(window))
        {
            return;
        }

        window.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        window.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
        window.RemoveHandler(InputElement.LostFocusEvent, OnLostFocus);
        window.Deactivated -= OnDeactivated;
        window.Closed -= OnClosed;
        panels.StopHeldActions();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        context.SelectionChanged -= OnSelectionChanged;

        foreach (var window in windows.ToArray())
        {
            Detach(window);
        }
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        captureKeys.Clear();
        panels.StopHeldActions();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        captureKeys.Clear();
        panels.StopHeldActions();
    }

    private void OnLostFocus(object? sender, RoutedEventArgs e)
    {
        captureKeys.Clear();
        panels.StopHeldActions();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            Detach(window);
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        captureKeys.Remove(e.Key);
        panels.ReleaseKey(e.Key);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var application = context.Application;

        if (e.Handled || application.IsRecordingShortcut || WorkbenchTextInput.IsComposing(e.Source))
        {
            return;
        }

        var settingsModifiers = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        if (e.Key == Key.OemComma && e.KeyModifiers == settingsModifiers)
        {
            openSettings();
            e.Handled = true;
            return;
        }

        if (isModal() || application.IsSettingsOpen || application.IsWorkspaceSettingsOpen || application.IsAddDialogOpen ||
            application.IsConfirmDeleteDialogOpen)
        {
            return;
        }

        var action = ShortcutBindingService.GetTriggeredActionKey(application.Settings.ShortcutBindings,
            e.Key, e.KeyModifiers);
        var inTextInput = e.Source is TextBox || e.Source is Control control &&
            control.FindAncestorOfType<TextBox>() is not null;

        if (action is null || inTextInput && action is not
            (ShortcutActionKeys.INPUT_CLEAR_DRAFT or ShortcutActionKeys.INPUT_CYCLE_MARK_GROUP or ShortcutActionKeys.LIVE_PLAYER_CAPTURE))
        {
            return;
        }

        if (action == ShortcutActionKeys.INPUT_CYCLE_MARK_GROUP && application.SelectedWorkspace is not null)
        {
            application.CycleWorkspaceMarkGroupCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (action == ShortcutActionKeys.LIVE_PLAYER_CAPTURE)
        {
            if (captureKeys.Add(e.Key))
            {
                _ = panels.CaptureCurrentFrameAsync();
            }
            e.Handled = true;
            return;
        }

        if (context.CurrentRoom is not { } room)
        {
            return;
        }

        switch (action)
        {
            case ShortcutActionKeys.LIVE_ROOM_START_LISTENING:
                room.StartListening();
                break;
            case ShortcutActionKeys.LIVE_ROOM_STOP_LISTENING:
                room.StopListening();
                break;
            case ShortcutActionKeys.LIVE_PLAYER_START:
                showPanel(WorkbenchPanelIds.LIVE_PLAYER);
                _ = room.PlayLiveAsync();
                break;
            case ShortcutActionKeys.LIVE_PLAYER_STOP:
                room.StopLivePlayer();
                break;
            case ShortcutActionKeys.LIVE_PLAYER_CHASE:
                showPanel(WorkbenchPanelIds.LIVE_PLAYER);
                _ = room.ChaseLiveAsync();
                break;
            case ShortcutActionKeys.INPUT_FOCUS_DRAFT:
                panels.FocusInputDraft();
                break;
            case ShortcutActionKeys.INPUT_CLEAR_DRAFT:
                panels.ClearInputDraft();
                break;
            case ShortcutActionKeys.LYRIC_START_SENDING:
                _ = room.StartAutoLyricAsync();
                break;
            case ShortcutActionKeys.LYRIC_STOP_SENDING:
                room.StopAutoLyric();
                break;
            case ShortcutActionKeys.LYRIC_SEEK_BACKWARD:
                panels.StartLyricSeek(e.Key, -0.5);
                break;
            case ShortcutActionKeys.LYRIC_SEEK_FORWARD:
                panels.StartLyricSeek(e.Key, 0.5);
                break;
            case ShortcutActionKeys.LYRIC_SEEK_NEXT:
                panels.SeekNextLyric(e.Key);
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
