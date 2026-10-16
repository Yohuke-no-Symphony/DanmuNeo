using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;

namespace Yohuke.DanmuNeo.Windowing;

internal static class WorkbenchTextInput
{
    internal static bool IsComposing(object? source)
    {
        var textBox = source as TextBox ?? (source as Control)?.FindAncestorOfType<TextBox>();
        return textBox?.GetVisualDescendants().OfType<TextPresenter>()
            .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText)) == true;
    }
}
