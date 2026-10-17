using Avalonia;
using Avalonia.Controls;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Windowing;

internal sealed class WorkbenchWindowRegistry : IDisposable
{
    private readonly Window mainWindow;
    private readonly HashSet<Window> businessWindows = [];
    private readonly Dictionary<string, WorkbenchAuxiliaryWindow> auxiliaryWindows = [];
    private readonly Dictionary<string, WorkbenchLocalizedBindings> localizedBindings = [];
    private readonly List<Window> modalWindows = [];
    private bool isDisposed;
    private bool shutdownPending;

    internal WorkbenchWindowRegistry(Window mainWindow)
    {
        this.mainWindow = mainWindow;
        ActiveWindow = mainWindow;
        mainWindow.PropertyChanged += OnMainWindowPropertyChanged;
        Localization.LanguageChanged += OnLanguageChanged;
    }

    internal event Action<Window>? BusinessWindowAdded;
    internal event Action<Window>? BusinessWindowRemoved;
    internal event Action? ModalChanged;
    internal bool IsModal => modalWindows.Count != 0;
    internal bool IsInteractionBlocked => shutdownPending || IsModal;
    internal bool IsShutdownPending => shutdownPending;
    internal Window ActiveWindow { get; private set; }
    internal Window CurrentDialogOwner => modalWindows.LastOrDefault() ?? ActiveWindow;
    internal IReadOnlyCollection<Window> BusinessWindows => businessWindows;

    internal void SetShutdownPending(bool value)
    {
        shutdownPending = value;
        RefreshInteraction();
    }

    internal void RegisterBusinessWindow(Window window)
    {
        if (isDisposed || !businessWindows.Add(window))
        {
            return;
        }

        if (!ReferenceEquals(window, mainWindow))
        {
            window.Topmost = GetEffectiveTopmost(window);
            window.PropertyChanged += OnBusinessWindowPropertyChanged;
        }

        window.Closed += BusinessWindowOnClosed;
        window.Activated += OnActivated;
        BusinessWindowAdded?.Invoke(window);
        RefreshInteraction();
    }

    internal void UnregisterBusinessWindow(Window window)
    {
        if (!businessWindows.Remove(window))
        {
            return;
        }

        window.Closed -= BusinessWindowOnClosed;
        window.Activated -= OnActivated;
        window.PropertyChanged -= OnBusinessWindowPropertyChanged;
        BusinessWindowRemoved?.Invoke(window);
    }

    internal Window OpenAuxiliary(
        string key,
        string title,
        Func<Control> createContent,
        bool modal = false,
        Func<Task>? beforeClose = null,
        Window? owner = null,
        double width = 640,
        double height = 480,
        WorkbenchAuxiliarySizing sizing = WorkbenchAuxiliarySizing.Resizable)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (auxiliaryWindows.TryGetValue(key, out var existing))
        {
            existing.Activate();
            return existing;
        }

        var window =
            new WorkbenchAuxiliaryWindow(owner ?? mainWindow, createContent(), width, height, sizing, beforeClose)
            {
                Title = title,
                Topmost = GetEffectiveTopmost(owner ?? mainWindow)
            };
        auxiliaryWindows.Add(key, window);
        window.Activated += OnActivated;

        if (modal)
        {
            modalWindows.Add(window);
        }

        window.Closed += (_, _) =>
        {
            auxiliaryWindows.Remove(key);
            modalWindows.Remove(window);
            window.Activated -= OnActivated;
            ActiveWindow = owner ?? mainWindow;
            RefreshInteraction();
        };
        window.Show(owner ?? mainWindow);
        RefreshInteraction();
        window.Activate();
        return window;
    }

    internal Window OpenLocalizedAuxiliary(
        string key,
        string titleKey,
        Func<WorkbenchLocalizedBindings, Control> createContent,
        bool modal = false,
        Func<Task>? beforeClose = null,
        Window? owner = null,
        double width = 640,
        double height = 480,
        WorkbenchAuxiliarySizing sizing = WorkbenchAuxiliarySizing.Resizable)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (auxiliaryWindows.TryGetValue(key, out var existing))
        {
            existing.Activate();
            return existing;
        }

        var bindings = new WorkbenchLocalizedBindings();

        try
        {
            var window = (WorkbenchAuxiliaryWindow)OpenAuxiliary(key, Localization.Get(titleKey),
                () => createContent(bindings), modal, beforeClose, owner, width, height, sizing);
            window.SetLocalizedTitle(titleKey);
            localizedBindings.Add(key, bindings);
            window.Closed += (_, _) =>
            {
                bindings.Dispose();
                localizedBindings.Remove(key);
            };
            return window;
        }
        catch
        {
            bindings.Dispose();
            throw;
        }
    }

    internal Window? FindAuxiliary(string key)
    {
        return auxiliaryWindows.GetValueOrDefault(key);
    }

    internal void CloseAuxiliary(string key)
    {
        FindAuxiliary(key)?.Close();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        mainWindow.PropertyChanged -= OnMainWindowPropertyChanged;
        Localization.LanguageChanged -= OnLanguageChanged;

        foreach (var window in auxiliaryWindows.Values.Reverse().ToArray())
        {
            window.Close();
        }

        foreach (var window in businessWindows.ToArray())
        {
            UnregisterBusinessWindow(window);
        }

        foreach (var bindings in localizedBindings.Values)
        {
            bindings.Dispose();
        }

        localizedBindings.Clear();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var window in auxiliaryWindows.Values)
        {
            window.RefreshLocalization();
        }
    }

    private void BusinessWindowOnClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            UnregisterBusinessWindow(window);
        }
    }

    private void OnMainWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property != WindowBase.TopmostProperty)
        {
            return;
        }

        RefreshTopmost();
    }

    private void OnBusinessWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == WorkbenchWindowTopmost.IsIndependentlyTopmostProperty)
        {
            RefreshTopmost();
        }
    }

    private bool GetEffectiveTopmost(Window window)
    {
        if (mainWindow.Topmost)
        {
            return true;
        }

        if (ReferenceEquals(window, mainWindow))
        {
            return false;
        }

        if (businessWindows.Contains(window))
        {
            return WorkbenchWindowTopmost.GetIsIndependentlyTopmost(window);
        }

        return window.Owner is Window owner && GetEffectiveTopmost(owner);
    }

    private void RefreshTopmost()
    {
        foreach (var window in businessWindows.Concat(auxiliaryWindows.Values).ToArray())
        {
            if (!ReferenceEquals(window, mainWindow))
            {
                window.Topmost = GetEffectiveTopmost(window);
            }
        }
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            ActiveWindow = window;
        }
    }

    private void RefreshInteraction()
    {
        var modal = modalWindows.LastOrDefault();

        foreach (var window in businessWindows)
        {
            window.IsEnabled = !shutdownPending && modal is null;
        }

        foreach (var window in auxiliaryWindows.Values)
        {
            window.IsEnabled = !shutdownPending && (modal is null || ReferenceEquals(window, modal));
        }

        ModalChanged?.Invoke();
    }
}
