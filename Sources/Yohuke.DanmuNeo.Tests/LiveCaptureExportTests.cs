using Avalonia.Headless.XUnit;
using SkiaSharp;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证 PNG 合成导出不会破坏永久原图及记录。</summary>
public sealed class LiveCaptureExportTests
{
    /// <summary>导出附加备注和三个时间并保留永久原图及图库记录。</summary>
    [AvaloniaFact]
    public async Task ExportAddsNotesAndAllThreeTimesWithoutChangingOriginalOrRemovingRecords()
    {
        var directory = Path.Combine(Path.GetTempPath(), "danmu-capture-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LiveCaptureStore(Path.Combine(directory, "Captures"));
            var frame = LiveCaptureStoreTests.CreateFrame();
            using (var image = new SKBitmap(640, 360))
            {
                image.Erase(SKColors.Red);
                using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
                frame = frame with { PngBytes = encoded.ToArray(), Width = 640, Height = 360 };
            }
            var record = store.Add(frame, "room");
            store.UpdateNote(record.Id, "exported note");
            await store.FlushAsync();
            var original = await File.ReadAllBytesAsync(store.GetOriginalPath(record.Id));
            var exporter = new LiveCaptureExportService(store);
            var paths = await exporter.ExportAsync(store.Records, Path.Combine(directory, "exports"));
            Assert.Single(paths);
            using var rendered = SKBitmap.Decode(paths[0]);
            Assert.Equal(640, rendered.Width);
            Assert.True(rendered.Height > 360);
            Assert.Equal(SKColors.Red, rendered.GetPixel(10, 10));
            Assert.Equal(original, await File.ReadAllBytesAsync(store.GetOriginalPath(record.Id)));
            Assert.Single(store.Records);
            await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportToFileAsync(store.Records[0], store.GetOriginalPath(record.Id)));
            Assert.Equal(original, await File.ReadAllBytesAsync(store.GetOriginalPath(record.Id)));
            Assert.Contains("exported note", LiveCaptureTimeFormatter.CreateExportText(store.Records[0]));
            Assert.Contains("01:00:00", LiveCaptureTimeFormatter.CreateExportText(store.Records[0]));
            using var canceled = new System.Threading.CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporter.ExportAsync(store.Records, Path.Combine(directory, "canceled"), canceled.Token));
            Assert.Single(store.Records);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
