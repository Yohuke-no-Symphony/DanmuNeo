using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>由 View 独占解码和释放本地原图，避免 Bitmap 进入图库 ViewModel。</summary>
public sealed class LiveCaptureImage : Image
{
    public static readonly StyledProperty<string?> FilePathProperty =
        AvaloniaProperty.Register<LiveCaptureImage, string?>(nameof(FilePath));
    public static readonly StyledProperty<int> DecodeWidthProperty =
        AvaloniaProperty.Register<LiveCaptureImage, int>(nameof(DecodeWidth), 640);

    private Bitmap? bitmap;
    private bool attached;

    public string? FilePath
    {
        get => GetValue(FilePathProperty);
        set => SetValue(FilePathProperty, value);
    }

    public int DecodeWidth
    {
        get => GetValue(DecodeWidthProperty);
        set => SetValue(DecodeWidthProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        LoadImage();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = false;
        ReleaseImage();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (attached && (change.Property == FilePathProperty || change.Property == DecodeWidthProperty))
        {
            LoadImage();
        }
    }

    private void LoadImage()
    {
        ReleaseImage();
        if (string.IsNullOrEmpty(FilePath))
        {
            return;
        }
        try
        {
            using var stream = File.OpenRead(FilePath);
            Span<byte> header = stackalloc byte[24];
            stream.ReadExactly(header);
            var dimensions = LiveCapturePngValidator.ValidateHeader(header);
            stream.Position = 0;
            bitmap = Bitmap.DecodeToWidth(stream, Math.Min(dimensions.Width, Math.Clamp(DecodeWidth, 1, 1920)));
            Source = bitmap;
            ToolTip.SetTip(this, null);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            ToolTip.SetTip(this, Localization.Get("Captures.PreviewFailed"));
        }
    }

    private void ReleaseImage()
    {
        Source = null;
        bitmap?.Dispose();
        bitmap = null;
    }
}
