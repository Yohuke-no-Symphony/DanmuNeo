using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Views;

internal class AttachedDialogCoordinator : IDisposable
{
    private readonly Window owner;
    private readonly Control dialogAnchor;
    private MainWindowViewModel? viewModel;
    private AttachedDialogWindow? addDialogWindow;
    private AttachedDialogWindow? settingsDialogWindow;
    private AttachedDialogWindow? confirmDeleteDialogWindow;
    private AttachedDialogWindow? updateDialogWindow;
    private bool isBoundsUpdateQueued;
    private bool isDisposed;

    public AttachedDialogCoordinator(Window owner, Control dialogAnchor)
    {
        this.owner = owner;
        this.dialogAnchor = dialogAnchor;

        owner.PositionChanged += (_, _) => RequestUpdateDialogBounds();
        owner.SizeChanged += (_, _) => RequestUpdateDialogBounds();
        owner.PropertyChanged += Owner_OnPropertyChanged;
    }

    public void Attach(MainWindowViewModel? nextViewModel)
    {
        if (viewModel == nextViewModel)
        {
            return;
        }

        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        }

        viewModel = nextViewModel;

        if (viewModel is not null)
        {
            viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        }

        SyncDialogs();
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        Attach(null);
        CloseDialog(ref addDialogWindow);
        CloseDialog(ref settingsDialogWindow);
        CloseDialog(ref confirmDeleteDialogWindow);
        CloseDialog(ref updateDialogWindow);
    }

    private void Owner_OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            SyncDialogs();
        }
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IsAddDialogOpen) or
            nameof(MainWindowViewModel.IsSettingsOpen) or
            nameof(MainWindowViewModel.IsConfirmDeleteDialogOpen) or
            nameof(MainWindowViewModel.IsUpdateDialogOpen))
        {
            SyncDialogs();
        }
    }

    private void SyncDialogs()
    {
        if (isDisposed || viewModel is null)
        {
            CloseDialog(ref addDialogWindow);
            CloseDialog(ref settingsDialogWindow);
            CloseDialog(ref confirmDeleteDialogWindow);
            CloseDialog(ref updateDialogWindow);
            return;
        }

        if (owner.WindowState == WindowState.Minimized)
        {
            HideDialog(addDialogWindow);
            HideDialog(settingsDialogWindow);
            HideDialog(confirmDeleteDialogWindow);
            HideDialog(updateDialogWindow);
            return;
        }

        SyncDialog(ref addDialogWindow, viewModel.IsAddDialogOpen, () => new AddWorkspaceDialogView());
        SyncDialog(ref settingsDialogWindow, viewModel.IsSettingsOpen, () => new SettingsDialogView());
        SyncDialog(ref confirmDeleteDialogWindow, viewModel.IsConfirmDeleteDialogOpen, () => new ConfirmDeleteDialogView());
        SyncDialog(ref updateDialogWindow, viewModel.IsUpdateDialogOpen, () => new AppUpdateDialogView(),
            () => viewModel?.CloseUpdateDialog());
    }

    private void SyncDialog(
        ref AttachedDialogWindow? window,
        bool isOpen,
        Func<Control> createContent,
        Action? onClosed = null)
    {
        if (!isOpen)
        {
            CloseDialog(ref window);
            return;
        }

        if (viewModel is null)
        {
            return;
        }

        if (window is null)
        {
            var content = createContent();
            content.DataContext = viewModel;

            window = new()
            {
                DataContext = viewModel,
                Content = content
            };

            if (onClosed is not null)
            {
                window.Closed += (_, _) => onClosed();
            }

            window.Show(owner);
            window.Activate();
        }
        else
        {
            window.DataContext = viewModel;

            if (window.Content is Control content)
            {
                content.DataContext = viewModel;
            }

            if (!window.IsVisible)
            {
                window.Show(owner);
                window.Activate();
            }
        }

        UpdateDialogBounds(window);
    }

    private void UpdateDialogBounds()
    {
        UpdateDialogBounds(addDialogWindow);
        UpdateDialogBounds(settingsDialogWindow);
        UpdateDialogBounds(confirmDeleteDialogWindow);
        UpdateDialogBounds(updateDialogWindow);
    }

    private void RequestUpdateDialogBounds()
    {
        if (isDisposed || isBoundsUpdateQueued)
        {
            return;
        }

        isBoundsUpdateQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            isBoundsUpdateQueued = false;
            UpdateDialogBounds();
        }, DispatcherPriority.Render);
    }

    private void UpdateDialogBounds(AttachedDialogWindow? window)
    {
        if (window is null || !window.IsVisible)
        {
            return;
        }

        var bounds = dialogAnchor.Bounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var nextPosition = dialogAnchor.PointToScreen(new(0, 0));

        if (window.Position != nextPosition)
        {
            window.Position = nextPosition;
        }

        if (!AreClose(window.Width, bounds.Width))
        {
            window.Width = bounds.Width;
        }

        if (!AreClose(window.Height, bounds.Height))
        {
            window.Height = bounds.Height;
        }
    }

    private static bool AreClose(double first, double second)
    {
        return Math.Abs(first - second) < 0.5;
    }

    private static void HideDialog(AttachedDialogWindow? window)
    {
        if (window?.IsVisible == true)
        {
            window.Hide();
        }
    }

    private static void CloseDialog(ref AttachedDialogWindow? window)
    {
        var currentWindow = window;
        window = null;

        if (currentWindow is not null)
        {
            currentWindow.Close();
        }
    }
}
