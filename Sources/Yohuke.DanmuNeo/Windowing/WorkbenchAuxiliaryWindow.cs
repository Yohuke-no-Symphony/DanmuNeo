using Avalonia.Controls;

namespace Yohuke.DanmuNeo.Windowing;

internal sealed class WorkbenchAuxiliaryWindow : Window
{
    private readonly Func<Task>? beforeClose;
    private bool closing;
    private bool allowClose;

    internal WorkbenchAuxiliaryWindow(Func<Task>? beforeClose)
    {
        this.beforeClose = beforeClose;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
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
            closing = false;

            if (content is not null)
            {
                content.IsEnabled = true;
            }
        }
    }
}
