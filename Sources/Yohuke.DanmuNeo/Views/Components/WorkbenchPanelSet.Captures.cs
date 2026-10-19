using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Threading;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views.Components;

public sealed partial class WorkbenchPanelSet
{
    private readonly HashSet<ShortcutBindingState> captureBindings = [];
    private LiveRoomTabViewModel? captureRoom;
    private CancellationTokenSource? captureCancellation;
    private CancellationTokenSource? captureNoteCancellation;
    private Task captureTask = Task.CompletedTask;
    private IDisposable? captureButtonBinding;
    private Action<string>? capturePanelActivator;
    private Func<LiveCaptureItemViewModel, CancellationToken, Task<string?>>? captureNoteEditor;
    private bool captureInteractionBlocked;
    private bool capturesDisposed;

    /// <summary>从玩家按钮、图库及快捷键进入同一个有来源校验的抓帧流程。</summary>
    public Task CaptureCurrentFrameAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        var gallery = context.Application.LiveCaptures;
        if (capturesDisposed || captureInteractionBlocked || context.Application.IsRecordingShortcut ||
            !gallery.CanCapture || context.CaptureSelection() is not { } selection)
        {
            return Task.CompletedTask;
        }

        var cancellation = new CancellationTokenSource();
        captureCancellation = cancellation;
        gallery.IsCapturing = true;
        captureTask = CaptureFrameCoreAsync(selection, selection.Room.RoomName, cancellation);
        return captureTask;
    }

    internal async Task CompletePendingCaptureAsync()
    {
        captureCancellation?.Cancel();
        captureNoteCancellation?.Cancel();
        await captureTask;
    }

    internal void SetCaptureNoteEditor(Func<LiveCaptureItemViewModel, CancellationToken, Task<string?>> editor)
    {
        captureNoteEditor = editor;
    }

    private void InitializeCaptureBindings()
    {
        Player.CaptureRequested += OnCaptureRequested;
        Player.CaptureAvailabilityChanged += OnCaptureAvailabilityChanged;
        LiveCaptures.CaptureRequested += OnCaptureRequested;
        context.Application.PropertyChanged += OnCaptureApplicationChanged;
        captureButtonBinding = Player.FindControl<Button>("FrameCaptureButton")!.Bind(
            InputElement.IsEnabledProperty,
            new Binding(nameof(LiveCapturesViewModel.CanCapture)) { Source = context.Application.LiveCaptures });
        RefreshCaptureState();
    }

    private async Task CaptureFrameCoreAsync(WorkbenchSelection selection, string roomName,
        CancellationTokenSource cancellation)
    {
        var gallery = context.Application.LiveCaptures;
        try
        {
            var frame = await Player.CaptureFrameAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!context.IsCurrent(selection))
            {
                throw new OperationCanceledException(Localization.Get("Captures.SessionChanged"));
            }

            var item = await gallery.CaptureAsync(frame, roomName);
            if (gallery.HasPendingOriginals && !capturesDisposed)
            {
                ShowCaptureSaveFailure("Captures.PendingSaveStatus");
                return;
            }
            if (capturesDisposed || captureInteractionBlocked || cancellation.IsCancellationRequested)
            {
                return;
            }

            captureCancellation = null;
            await EditCapturedNoteAsync(item);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (!capturesDisposed)
            {
                gallery.ReportCaptureFailure(AppLogSanitizer.Sanitize(LocalizedExceptionText.Get(exception)));
                context.Application.Log.Error(Localization.Get("Captures.Source"),
                    Localization.Get("Captures.CaptureFailedLog"), exception);
            }
        }
        finally
        {
            if (ReferenceEquals(captureCancellation, cancellation))
            {
                captureCancellation = null;
            }
            cancellation.Dispose();
            if (!capturesDisposed)
            {
                gallery.IsCapturing = false;
            }
        }
    }

    private async Task EditCapturedNoteAsync(LiveCaptureItemViewModel item)
    {
        if (captureNoteEditor is not { } editor)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        captureNoteCancellation = cancellation;
        var gallery = context.Application.LiveCaptures;
        try
        {
            var note = await editor(item, cancellation.Token);
            if (note is not null && !capturesDisposed)
            {
                item.Note = note;
                await gallery.SaveAsync();
                if (gallery.HasPendingChanges)
                {
                    ShowCaptureSaveFailure("Captures.PendingNoteSaveStatus");
                }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!capturesDisposed)
            {
                gallery.ReportNoteFailure(AppLogSanitizer.Sanitize(LocalizedExceptionText.Get(exception)));
                ShowCaptureSaveFailure("Captures.PendingNoteSaveStatus");
            }
        }
        finally
        {
            captureNoteCancellation = null;
        }
    }

    private void ShowCaptureSaveFailure(string statusKey)
    {
        context.Application.SetLocalizedStatus(statusKey);
        context.Application.Log.Error(Localization.Get("Captures.Source"), context.Application.LiveCaptures.StatusText);
        if (!captureInteractionBlocked)
        {
            capturePanelActivator?.Invoke(WorkbenchPanelIds.LIVE_CAPTURES);
        }
    }

    private void RefreshCaptureState()
    {
        if (capturesDisposed)
        {
            return;
        }

        if (!ReferenceEquals(captureRoom, context.CurrentRoom))
        {
            if (captureRoom is not null)
            {
                captureRoom.PropertyChanged -= OnCaptureRoomChanged;
            }

            captureRoom = context.CurrentRoom;
            if (captureRoom is not null)
            {
                captureRoom.PropertyChanged += OnCaptureRoomChanged;
            }
        }

        var application = context.Application;
        var blocked = captureInteractionBlocked || application.IsRecordingShortcut || application.IsSettingsOpen ||
                      application.IsWorkspaceSettingsOpen || application.IsAddDialogOpen || application.IsConfirmDeleteDialogOpen;
        application.LiveCaptures.SetCaptureSourceAvailable(!blocked && Player.CanCaptureFrame);
        foreach (var binding in captureBindings.Except(application.Settings.ShortcutBindings).ToArray())
        {
            binding.PropertyChanged -= OnCaptureBindingChanged;
            captureBindings.Remove(binding);
        }
        foreach (var binding in application.Settings.ShortcutBindings)
        {
            if (captureBindings.Add(binding))
            {
                binding.PropertyChanged += OnCaptureBindingChanged;
            }
        }

        var selected = application.Settings.ShortcutBindings.FirstOrDefault(binding =>
            binding.ActionKey == ShortcutActionKeys.LIVE_PLAYER_CAPTURE && binding.IsEnabled);
        var conflicts = ShortcutBindingService.GetConflictActionKeys(application.Settings.ShortcutBindings);
        Player.SetCaptureGesture(!blocked && selected is not null && !conflicts.Contains(selected.ActionKey)
            ? selected.GestureText : null);
    }

    private void OnCaptureRequested(object? sender, EventArgs e)
    {
        _ = CaptureCurrentFrameAsync();
    }

    private void OnCaptureAvailabilityChanged(object? sender, EventArgs e)
    {
        RefreshCaptureState();
    }

    private void OnCaptureRoomChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LiveRoomTabViewModel.LivePlayerUrl) or nameof(LiveRoomTabViewModel.IsLivePlayerVisible))
        {
            captureCancellation?.Cancel();
            RefreshCaptureState();
        }
    }

    private void OnCaptureApplicationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IsRecordingShortcut) or nameof(MainWindowViewModel.IsSettingsOpen) or
            nameof(MainWindowViewModel.IsWorkspaceSettingsOpen) or nameof(MainWindowViewModel.IsAddDialogOpen) or
            nameof(MainWindowViewModel.IsConfirmDeleteDialogOpen))
        {
            RefreshCaptureState();
        }
    }

    private void OnCaptureBindingChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshCaptureState();
    }

    private void DisposeCaptureBindings()
    {
        if (capturesDisposed)
        {
            return;
        }

        capturesDisposed = true;
        captureCancellation?.Cancel();
        captureNoteCancellation?.Cancel();
        Player.CaptureRequested -= OnCaptureRequested;
        Player.CaptureAvailabilityChanged -= OnCaptureAvailabilityChanged;
        LiveCaptures.CaptureRequested -= OnCaptureRequested;
        context.Application.PropertyChanged -= OnCaptureApplicationChanged;
        if (captureRoom is not null)
        {
            captureRoom.PropertyChanged -= OnCaptureRoomChanged;
        }
        foreach (var binding in captureBindings)
        {
            binding.PropertyChanged -= OnCaptureBindingChanged;
        }
        captureBindings.Clear();
        captureButtonBinding?.Dispose();
        capturePanelActivator = null;
        captureNoteEditor = null;
        context.Application.LiveCaptures.SetCaptureSourceAvailable(false);
        Player.SetCaptureGesture(null);
    }
}
