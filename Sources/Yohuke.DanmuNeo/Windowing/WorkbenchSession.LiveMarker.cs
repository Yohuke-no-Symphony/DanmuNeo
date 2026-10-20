using System.ComponentModel;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Windowing;

internal sealed partial class WorkbenchSession
{
    private LiveMarkerViewModel? liveMarkerTitleSource;

    private void InitializeLiveMarkerPresentation()
    {
        Context.PropertyChanged += OnLiveMarkerContextChanged;
        ObserveLiveMarkerTitleSource();
    }

    private void ReleaseLiveMarkerPresentation()
    {
        Context.PropertyChanged -= OnLiveMarkerContextChanged;
        if (liveMarkerTitleSource is not null)
        {
            liveMarkerTitleSource.PropertyChanged -= OnLiveMarkerDocumentChanged;
            liveMarkerTitleSource = null;
        }
    }

    private void OnLiveMarkerContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkbenchContext.CurrentRoom))
        {
            ObserveLiveMarkerTitleSource();
        }
    }

    private void ObserveLiveMarkerTitleSource()
    {
        if (liveMarkerTitleSource is not null)
        {
            liveMarkerTitleSource.PropertyChanged -= OnLiveMarkerDocumentChanged;
        }

        liveMarkerTitleSource = Context.CurrentRoom?.LiveMarker;
        if (liveMarkerTitleSource is not null)
        {
            liveMarkerTitleSource.PropertyChanged += OnLiveMarkerDocumentChanged;
        }

        UpdateLiveMarkerPresentation();
    }

    private void OnLiveMarkerDocumentChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LiveMarkerViewModel.HasDocument) or nameof(LiveMarkerViewModel.FileName))
        {
            UpdateLiveMarkerPresentation();
        }
    }

    private void UpdateLiveMarkerPresentation()
    {
        var panel = Layouts.PanelAdapters[WorkbenchPanelIds.LIVE_MARKER];
        var title = liveMarkerTitleSource is { HasDocument: true } marker
            ? Localization.Format("Workbench.Panel.LiveMarkerFile", marker.FileName)
            : Localization.Get("Workbench.Panel.LiveMarker");
        if (panel.Title == title)
        {
            return;
        }

        panel.Title = title;
        Layouts.RefreshFloatingTitles();
    }
}
