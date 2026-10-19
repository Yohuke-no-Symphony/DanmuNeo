using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Captures;

namespace Yohuke.DanmuNeo.Services;

/// <summary>以原图和当前备注合成新的 PNG，永久图库不受导出成功、失败或取消影响。</summary>
public sealed class LiveCaptureExportService(ILiveCaptureStore store) : ILiveCaptureExportService
{
    /// <summary>导出指定截图集合；目标文件不覆盖，已导出的文件不影响其余条目重试。</summary>
    public async Task<IReadOnlyList<string>> ExportAsync(IEnumerable<LiveCaptureRecord> records,
        string outputDirectory, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (store.ContainsPath(outputDirectory))
        {
            throw new InvalidOperationException(Localization.Get("Captures.ExportInsideGallery"));
        }
        var snapshots = records.ToArray();
        await store.FlushAsync(cancellationToken);
        Directory.CreateDirectory(outputDirectory);
        var outputs = new List<string>();
        foreach (var record in snapshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var png = await File.ReadAllBytesAsync(store.GetOriginalPath(record.Id), cancellationToken);
            var text = LiveCaptureTimeFormatter.CreateExportText(record);
            var composed = await Dispatcher.UIThread.InvokeAsync(() => Render(png, text), DispatcherPriority.Background,
                cancellationToken);
            var safeRoom = string.Concat(record.RoomId.Select(character => char.IsAsciiLetterOrDigit(character) ? character : '_'));
            var name = $"{safeRoom}-{record.CapturedAt:yyyyMMdd-HHmmss-fff}-{record.Id}.png";
            var path = Path.Combine(outputDirectory, name);
            if (File.Exists(path))
            {
                path = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(name) + "-" + Guid.NewGuid().ToString("N")[..8] + ".png");
            }
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, composed, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, path, false);
                outputs.Add(path);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
        return outputs;
    }

    /// <summary>导出单张到文件选择器指定的位置，永久图库目录内禁止覆盖写入。</summary>
    public async Task ExportToFileAsync(LiveCaptureRecord record, string filePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (store.ContainsPath(filePath))
        {
            throw new InvalidOperationException(Localization.Get("Captures.ExportInsideGallery"));
        }
        await store.FlushAsync(cancellationToken);
        var original = await File.ReadAllBytesAsync(store.GetOriginalPath(record.Id), cancellationToken);
        var text = LiveCaptureTimeFormatter.CreateExportText(record);
        var composed = await Dispatcher.UIThread.InvokeAsync(() => Render(original, text), DispatcherPriority.Background,
            cancellationToken);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, composed, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, filePath, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static byte[] Render(byte[] png, string text)
    {
        LiveCapturePngValidator.Validate(png);
        using var input = new MemoryStream(png, false);
        using var bitmap = new Bitmap(input);
        var width = bitmap.PixelSize.Width;
        var imageHeight = bitmap.PixelSize.Height;
        var caption = new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            FontSize = Math.Clamp(width / 48.0, 12, 28),
            TextWrapping = TextWrapping.Wrap,
            Margin = new(12)
        };
        var footer = new Border { Background = new SolidColorBrush(Color.FromRgb(18, 22, 30)), Child = caption };
        footer.Measure(new(width, double.PositiveInfinity));
        var footerHeight = Math.Max(1, (int)Math.Ceiling(footer.DesiredSize.Height));
        var height = imageHeight + footerHeight;
        if (height > 16384 || (long)width * height > 68_000_000)
        {
            throw new InvalidDataException(Localization.Get("Captures.ExportTooLarge"));
        }
        var visual = new Grid { RowDefinitions = new($"{imageHeight},{footerHeight}"), Width = width, Height = height };
        visual.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Fill });
        Grid.SetRow(footer, 1);
        visual.Children.Add(footer);
        visual.Measure(new(width, height));
        visual.Arrange(new(0, 0, width, height));
        using var target = new RenderTargetBitmap(new(width, height), new(96, 96));
        target.Render(visual);
        using var output = new MemoryStream();
        target.Save(output);
        return output.ToArray();
    }
}
