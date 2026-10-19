using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证图库面板无房间时的可用性与备注绑定。</summary>
public sealed class LiveCapturesPanelViewTests
{
    /// <summary>未选房间仍可浏览图库，选中截图后才允许修改备注。</summary>
    [AvaloniaFact]
    public async Task GalleryStaysUsableWithoutRoomAndEnablesNoteOnlyForSelectedCapture()
    {
        var directory = Path.Combine(Path.GetTempPath(), "danmu-gallery-ui-" + Guid.NewGuid().ToString("N"));
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        fixture.ViewModel.SelectedWorkspace!.SelectedRoom = null;
        var store = new LiveCaptureStore(directory);
        using var viewModel = new LiveCapturesViewModel(store, new LiveCaptureExportService(store));
        using var panel = new LiveCapturesPanelView(context, viewModel);
        var window = new Window { Width = 700, Height = 600, Content = panel };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(panel.IsEnabled);
            Assert.Same(viewModel, panel.DataContext);
            Assert.False(panel.FindControl<Button>("CaptureButton")!.IsEnabled);
            var note = panel.FindControl<TextBox>("NoteEditor")!;
            Assert.False(note.IsEnabled);
            await viewModel.CaptureAsync(LiveCaptureStoreTests.CreateFrame(), "room");
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.True(note.IsEnabled);
            var previews = panel.GetVisualDescendants().OfType<LiveCaptureImage>().Select(image => image.Source).OfType<Bitmap>().ToArray();
            Assert.NotEmpty(previews);
            Assert.All(previews, image => Assert.Equal(1, image.PixelSize.Width));
            note.Text = "edited from view";
            await viewModel.FlushAsync();
            Assert.Equal("edited from view", Assert.Single(store.Records).Note);
            Assert.True(panel.IsEnabled);
        }
        finally
        {
            window.Close();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
