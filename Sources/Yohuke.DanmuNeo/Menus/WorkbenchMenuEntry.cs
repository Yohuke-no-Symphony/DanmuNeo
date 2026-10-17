using System.Windows.Input;
using Avalonia.Input;
using Lucide.Avalonia;

namespace Yohuke.DanmuNeo.Menus;

internal sealed record WorkbenchMenuEntry(
    string Id,
    string Header,
    LucideIconKind? Icon = null,
    ICommand? Command = null,
    bool IsChecked = false,
    bool IsCheckable = false,
    KeyGesture? Gesture = null,
    IReadOnlyList<WorkbenchMenuEntry>? Children = null);
