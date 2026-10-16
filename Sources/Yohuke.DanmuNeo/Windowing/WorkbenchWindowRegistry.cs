using Avalonia.Controls;

namespace Yohuke.DanmuNeo.Windowing;

internal sealed class WorkbenchWindowRegistry : IDisposable
{
    private readonly Window mainWindow;
    private readonly HashSet<Window> businessWindows = [];
    private readonly Dictionary<string, WorkbenchAuxiliaryWindow> auxiliaryWindows = [];
    private readonly List<Window> modalWindows = [];
    private bool isDisposed;
    private bool shutdownPending;

    internal WorkbenchWindowRegistry(Window mainWindow)
    {
        this.mainWindow = mainWindow;
        ActiveWindow = mainWindow;
    }

    internal event Action<Window>? BusinessWindowAdded;
    internal event Action<Window>? BusinessWindowRemoved;
    internal event Action? ModalChanged;
    internal bool IsModal => modalWindows.Count != 0;
    internal bool IsInteractionBlocked => shutdownPending || IsModal;
    internal bool IsShutdownPending => shutdownPending;
    internal Window ActiveWindow { get; private set; }
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
        double height = 480)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);

        if (auxiliaryWindows.TryGetValue(key, out var existing))
        {
            existing.Activate();
            return existing;
        }

        var window = new WorkbenchAuxiliaryWindow(beforeClose)
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = Math.Min(440, width),
            MinHeight = Math.Min(300, height),
            Content = createContent()
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

        foreach (var window in auxiliaryWindows.Values.Reverse().ToArray())
        {
            window.Close();
        }

        foreach (var window in businessWindows.ToArray())
        {
            UnregisterBusinessWindow(window);
        }
    }

    private void BusinessWindowOnClosed(object? sender, EventArgs e)
    {
        if (sender is Window window)
        {
            UnregisterBusinessWindow(window);
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
