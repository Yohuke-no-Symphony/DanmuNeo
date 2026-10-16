using System.Windows.Input;
using Avalonia.Input;

namespace Yohuke.DanmuNeo.Menus;

internal sealed record WorkbenchMenuEntry(
    string Id,
    string Header,
    ICommand? Command = null,
    bool IsChecked = false,
    bool IsCheckable = false,
    KeyGesture? Gesture = null,
    IReadOnlyList<WorkbenchMenuEntry>? Children = null);
