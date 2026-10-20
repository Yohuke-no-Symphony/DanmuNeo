using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Yohuke.DanmuNeo.Windowing;

internal static class WorkbenchTextInput
{
    internal static RoutedEvent<RoutedEventArgs> NativeContextInvalidatedEvent { get; } =
        RoutedEvent.Register<Window, RoutedEventArgs>("WorkbenchNativeInputContextInvalidated", RoutingStrategies.Bubble);

    internal static void RequestNativeContextRecovery(Control source)
    {
        source.RaiseEvent(new RoutedEventArgs(NativeContextInvalidatedEvent));
        RequestClientRequery(source);
    }

    internal static void RequestClientRequery(Control source)
    {
        source.RaiseEvent(new TextInputMethodClientRequeryRequestedEventArgs
        {
            RoutedEvent = InputMethod.TextInputMethodClientRequeryRequestedEvent
        });
    }

    internal static bool RestoreInputMethod(Window window, Func<bool> restoreNativeWindow, bool cancelComposition = false)
    {
        if (!window.IsActive || window.FocusManager?.GetFocusedElement() is not TextBox input ||
            !ReferenceEquals(TopLevel.GetTopLevel(input), window) || !input.IsEffectivelyEnabled || input.IsReadOnly ||
            !InputMethod.GetIsInputMethodEnabled(input) || (!cancelComposition && IsComposing(input)) || !restoreNativeWindow())
        {
            return false;
        }

        try
        {
            input.SetCurrentValue(InputMethod.IsInputMethodEnabledProperty, false);
            if (cancelComposition)
            {
                var query = new TextInputMethodClientRequestedEventArgs
                {
                    RoutedEvent = InputElement.TextInputMethodClientRequestedEvent
                };
                input.RaiseEvent(query);
                query.Client?.SetPreeditText(null);
            }
        }
        finally
        {
            input.SetCurrentValue(InputMethod.IsInputMethodEnabledProperty, true);
        }

        return true;
    }

    internal static bool IsComposing(object? source)
    {
        var textBox = source as TextBox ?? (source as Control)?.FindAncestorOfType<TextBox>();
        return textBox?.GetVisualDescendants().OfType<TextPresenter>()
            .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText)) == true;
    }
}
