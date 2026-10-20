using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.LiveMarker;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Windowing;

internal sealed partial class WorkbenchAuxiliaryCoordinator
{
    internal async Task<LiveMarkerLeaveDecision> ConfirmLiveMarkerLeaveAsync(LiveMarkerViewModel document)
    {
        if (disposed)
        {
            return LiveMarkerLeaveDecision.Cancel;
        }

        var completion = new TaskCompletionSource<LiveMarkerLeaveDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = LiveMarkerLeaveDecision.Cancel;
        var window = windows.OpenLocalizedAuxiliary("live-marker-unsaved", "LiveMarker.UnsavedTitle", bindings =>
        {
            var question = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var save = new Button { IsDefault = true };
            var discard = new Button();
            var cancel = new Button { IsCancel = true };
            bindings.Bind(question, TextBlock.TextProperty, () => Localization.Format("LiveMarker.UnsavedQuestion", document.FileName));
            bindings.Bind(save, ContentControl.ContentProperty, "LiveMarker.Save");
            bindings.Bind(discard, ContentControl.ContentProperty, "LiveMarker.Discard");
            bindings.Bind(cancel, ContentControl.ContentProperty, "Window.Cancel");
            save.Click += (_, _) =>
            {
                result = LiveMarkerLeaveDecision.Save;
                windows.CloseAuxiliary("live-marker-unsaved");
            };
            discard.Click += (_, _) =>
            {
                result = LiveMarkerLeaveDecision.Discard;
                windows.CloseAuxiliary("live-marker-unsaved");
            };
            cancel.Click += (_, _) => windows.CloseAuxiliary("live-marker-unsaved");
            return CreateDialogSurface(new StackPanel
            {
                Spacing = 20,
                Children =
                {
                    question,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { cancel, discard, save }
                    }
                }
            });
        }, true, owner: windows.CurrentDialogOwner, width: 520, sizing: WorkbenchAuxiliarySizing.ContentHeight);
        window.Closed += OnClosed;
        try
        {
            return await completion.Task;
        }
        finally
        {
            window.Closed -= OnClosed;
        }

        void OnClosed(object? sender, EventArgs args)
        {
            completion.TrySetResult(result);
        }
    }
}
