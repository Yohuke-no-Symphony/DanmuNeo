using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>编辑本次截图的独立备注草稿，确认前不修改图库。</summary>
public partial class CaptureNoteDialogView : UserControl
{
    /// <summary>初始化备注编辑界面。</summary>
    public CaptureNoteDialogView()
    {
        InitializeComponent();
        Loaded += (_, _) => NoteInput.Focus();
    }

    internal event EventHandler? Confirmed;
    internal event EventHandler? Skipped;
    internal string NoteText => NoteInput.Text ?? "";

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Skipped?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        Confirmed?.Invoke(this, EventArgs.Empty);
    }

    private void OnSkipClicked(object? sender, RoutedEventArgs e)
    {
        Skipped?.Invoke(this, EventArgs.Empty);
    }
}
