using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Yohuke.DanmuNeo.Models.Logging;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Windowing;

internal sealed class WorkbenchAuxiliaryWindow : Window
{
    private const double SCREEN_MARGIN = 24;
    private readonly Func<Task>? beforeClose;
    private readonly Window startupOwner;
    private readonly WorkbenchAuxiliarySizing sizing;
    private readonly double preferredWidth;
    private readonly double preferredMinHeight;
    private bool closing;
    private bool allowClose;
    private bool opened;
    private bool updatingConstraints;

    internal WorkbenchAuxiliaryWindow(Window owner, Control content, double width, double height,
        WorkbenchAuxiliarySizing sizing, Func<Task>? beforeClose)
    {
        this.beforeClose = beforeClose;
        startupOwner = owner;
        this.sizing = sizing;
        preferredWidth = width;
        preferredMinHeight = sizing == WorkbenchAuxiliarySizing.ContentHeight ? 0 : Math.Min(300, height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Width = width;
        MinWidth = Math.Min(440, width);
        MinHeight = preferredMinHeight;

        if (sizing == WorkbenchAuxiliarySizing.ContentHeight)
        {
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            Content = new ScrollViewer
            {
                Content = content,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
        }
        else
        {
            Height = height;
            Content = content;
        }

        UpdateScreenConstraints();
    }

    /// <inheritdoc/>
    protected override void OnOpened(EventArgs e)
    {
        opened = true;
        PositionChanged += OnPositionChanged;
        ScalingChanged += OnScreenChanged;
        Screens.Changed += OnScreenChanged;
        SizeChanged += OnScreenChanged;
        UpdateScreenConstraints();
        base.OnOpened(e);
    }

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        opened = false;
        PositionChanged -= OnPositionChanged;
        ScalingChanged -= OnScreenChanged;
        Screens.Changed -= OnScreenChanged;
        SizeChanged -= OnScreenChanged;
        base.OnClosed(e);
    }

    /// <inheritdoc/>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (e.Cancel || allowClose || beforeClose is null)
        {
            return;
        }

        e.Cancel = true;

        if (!closing)
        {
            _ = CloseAfterSavingAsync();
        }
    }

    private void OnScreenChanged(object? sender, EventArgs e)
    {
        UpdateScreenConstraints();
    }

    private void OnPositionChanged(object? sender, EventArgs e)
    {
        UpdateScreenConstraints(false);
    }

    private void UpdateScreenConstraints(bool correctPosition = true)
    {
        if (updatingConstraints)
        {
            return;
        }

        var screen = Screens.ScreenFromWindow(opened ? this : startupOwner) ?? Screens.Primary;

        if (screen is null)
        {
            return;
        }

        updatingConstraints = true;

        try
        {
            var frame = FrameSize ?? ClientSize;
            var decorationWidth = Math.Max(0, frame.Width - ClientSize.Width);
            var decorationHeight = Math.Max(0, frame.Height - ClientSize.Height);
            var maxWidth = Math.Max(1, screen.WorkingArea.Width / screen.Scaling - SCREEN_MARGIN * 2 - decorationWidth);
            var maxHeight = Math.Max(1, screen.WorkingArea.Height / screen.Scaling - SCREEN_MARGIN * 2 - decorationHeight);
            MinWidth = Math.Min(Math.Min(440, preferredWidth), maxWidth);
            MinHeight = Math.Min(preferredMinHeight, maxHeight);
            MaxWidth = maxWidth;
            MaxHeight = maxHeight;
            Width = Math.Min(sizing == WorkbenchAuxiliarySizing.ContentHeight ? preferredWidth : Width, maxWidth);

            if (sizing == WorkbenchAuxiliarySizing.Resizable && Height > maxHeight)
            {
                Height = maxHeight;
            }

            if (opened && correctPosition)
            {
                var margin = (int)Math.Ceiling(SCREEN_MARGIN * screen.Scaling);
                var minX = screen.WorkingArea.X + margin;
                var minY = screen.WorkingArea.Y + margin;
                var maxX = Math.Max(minX, screen.WorkingArea.Right - margin - (int)Math.Ceiling(frame.Width * screen.Scaling));
                var maxY = Math.Max(minY, screen.WorkingArea.Bottom - margin - (int)Math.Ceiling(frame.Height * screen.Scaling));
                var position = new PixelPoint(Math.Clamp(Position.X, minX, maxX), Math.Clamp(Position.Y, minY, maxY));

                if (Position != position)
                {
                    Position = position;
                }
            }
        }
        finally
        {
            updatingConstraints = false;
        }
    }

    private async Task CloseAfterSavingAsync()
    {
        closing = true;
        var content = Content as Control;

        if (content is not null)
        {
            content.IsEnabled = false;
        }

        try
        {
            await beforeClose!();
            allowClose = true;
            Close();
        }
        catch (Exception exception)
        {
            Title = $"保存失败：{exception.Message}";
            if (startupOwner.DataContext is MainWindowViewModel application)
            {
                application.LogErrorOnce("设置", "设置窗口关闭前保存失败", exception);
            }
            else
            {
                StartupLog.Append("辅助窗口关闭前保存失败", AppLogLevel.ERROR, "设置", exception);
            }

            closing = false;

            if (content is not null)
            {
                content.IsEnabled = true;
            }
        }
    }
}
