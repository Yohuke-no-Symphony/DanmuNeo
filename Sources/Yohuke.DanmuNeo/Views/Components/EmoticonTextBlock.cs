using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 在弹幕原文中内联显示本地缓存的表情图片。
/// </summary>
public sealed class EmoticonTextBlock : TextBlock
{
    private const double EMOTICON_HEIGHT = 20;
    private readonly Dictionary<string, Bitmap> images = new(StringComparer.Ordinal);
    private bool isAttached;

    /// <summary>
    /// 包含原始表情文本的弹幕正文。
    /// </summary>
    public static readonly StyledProperty<string?> ContentTextProperty =
        AvaloniaProperty.Register<EmoticonTextBlock, string?>(nameof(ContentText));

    /// <summary>
    /// 正文中的表情图片映射。
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<DanmuEmoticon>?> EmoticonsProperty =
        AvaloniaProperty.Register<EmoticonTextBlock, IReadOnlyList<DanmuEmoticon>?>(nameof(Emoticons));

    /// <summary>
    /// 包含原始表情文本的弹幕正文。
    /// </summary>
    public string? ContentText
    {
        get => GetValue(ContentTextProperty);
        set => SetValue(ContentTextProperty, value);
    }

    /// <summary>
    /// 正文中的表情图片映射。
    /// </summary>
    public IReadOnlyList<DanmuEmoticon>? Emoticons
    {
        get => GetValue(EmoticonsProperty);
        set => SetValue(EmoticonsProperty, value);
    }

    /// <inheritdoc/>
    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ContentTextProperty || change.Property == EmoticonsProperty)
        {
            RebuildContent();
        }
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        isAttached = true;
        RebuildContent();
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        isAttached = false;
        ReleaseContent();
    }

    private void RebuildContent()
    {
        ReleaseContent();
        var content = ContentText ?? "";
        var candidates = Emoticons?.Where(emoticon => !string.IsNullOrEmpty(emoticon.Text) &&
                                                     !string.IsNullOrWhiteSpace(emoticon.ImagePath))
            .OrderByDescending(emoticon => emoticon.Text.Length).ToArray() ?? [];

        if (!isAttached || candidates.Length == 0)
        {
            Inlines = null;
            Text = content;
            return;
        }

        Text = null;
        Inlines = new();
        var position = 0;

        while (position < content.Length)
        {
            DanmuEmoticon? match = null;
            var matchIndex = int.MaxValue;

            foreach (var candidate in candidates)
            {
                var index = content.IndexOf(candidate.Text, position, StringComparison.Ordinal);

                if (index >= 0 && index < matchIndex)
                {
                    match = candidate;
                    matchIndex = index;
                }
            }

            if (match is null)
            {
                Inlines.Add(new Run(content[position..]));
                break;
            }

            if (matchIndex > position)
            {
                Inlines.Add(new Run(content[position..matchIndex]));
            }

            var bitmap = GetImage(match.ImagePath);

            if (bitmap is null)
            {
                Inlines.Add(new Run(match.Text));
            }
            else
            {
                var image = new Image
                {
                    Source = bitmap,
                    Height = EMOTICON_HEIGHT,
                    Width = EMOTICON_HEIGHT * bitmap.PixelSize.Width / bitmap.PixelSize.Height,
                    Stretch = Stretch.Uniform
                };
                AutomationProperties.SetName(image, match.Text);
                Inlines.Add(new InlineUIContainer(image)
                {
                    BaselineAlignment = BaselineAlignment.Center
                });
            }

            position = matchIndex + match.Text.Length;
        }
    }

    private Bitmap? GetImage(string path)
    {
        if (images.TryGetValue(path, out var image))
        {
            return image;
        }

        try
        {
            using var stream = File.OpenRead(path);
            image = Bitmap.DecodeToHeight(stream, (int)(EMOTICON_HEIGHT * 2), BitmapInterpolationMode.HighQuality);
            images.Add(path, image);
            return image;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           ArgumentException or NotSupportedException or NullReferenceException)
        {
            System.Diagnostics.Trace.TraceWarning($"表情图片解码失败：{exception.GetType().Name}");
            return null;
        }
    }

    private void ReleaseContent()
    {
        Inlines?.Clear();

        foreach (var image in images.Values)
        {
            image.Dispose();
        }

        images.Clear();
    }
}
