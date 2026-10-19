using Avalonia.Threading;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Windowing;

internal sealed partial class WorkbenchAuxiliaryCoordinator
{
    internal async Task<string?> EditCaptureNoteAsync(LiveCaptureItemViewModel item, CancellationToken cancellationToken)
    {
        if (disposed || windows.IsInteractionBlocked || cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var view = new CaptureNoteDialogView { DataContext = item };
        string? result = null;
        view.Confirmed += (_, _) =>
        {
            result = view.NoteText;
            windows.CloseAuxiliary("capture-note");
        };
        view.Skipped += (_, _) => windows.CloseAuxiliary("capture-note");
        var window = windows.OpenLocalizedAuxiliary("capture-note", "Captures.Note", _ => view, true,
            owner: windows.CurrentDialogOwner, width: 520, sizing: WorkbenchAuxiliarySizing.ContentHeight);
        window.Closed += OnClosed;
        await using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(window.Close));
        try
        {
            return await completion.Task;
        }
        finally
        {
            window.Closed -= OnClosed;
        }

        void OnClosed(object? sender, EventArgs e)
        {
            completion.TrySetResult(result);
        }
    }
}
