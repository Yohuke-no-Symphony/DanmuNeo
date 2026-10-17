using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Windowing;

internal sealed class WorkbenchAuxiliaryCoordinator : IDisposable
{
    private readonly MainWindowViewModel application;
    private readonly WorkbenchWindowRegistry windows;
    private readonly WorkbenchLayoutController layouts;
    private bool disposed;

    internal WorkbenchAuxiliaryCoordinator(MainWindowViewModel application,
        WorkbenchWindowRegistry windows, WorkbenchLayoutController layouts)
    {
        this.application = application;
        this.windows = windows;
        this.layouts = layouts;
        application.PropertyChanged += OnApplicationChanged;
        SyncDialogs();
    }

    internal void OpenSettings()
    {
        if (windows.IsShutdownPending || disposed)
        {
            return;
        }

        application.OpenSettings();
        SyncDialogs();
        windows.FindAuxiliary("settings")?.Activate();
    }

    internal void OpenAbout()
    {
        windows.OpenLocalizedAuxiliary("about", "Window.About.Title", bindings =>
        {
            var link = new Button();
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
            var version = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var developer = new TextBlock { TextWrapping = TextWrapping.Wrap };
            bindings.Bind(link, ContentControl.ContentProperty, "Window.About.ProjectHome");
            bindings.Bind(error, TextBlock.TextProperty, "Window.About.BrowserFailed");
            bindings.Bind(version, TextBlock.TextProperty, () => Localization.Format("Window.About.Version", application.AppVersion));
            bindings.Bind(developer, TextBlock.TextProperty, "Window.About.Developer");
            link.Click += async (_, _) =>
            {
                try
                {
                    if (!await TopLevel.GetTopLevel(link)!.Launcher.LaunchUriAsync(
                            new("https://github.com/Yohuke-no-Symphony/DanmuNeo")))
                    {
                        error.IsVisible = true;
                        application.Log.Error(Localization.Get("Workbench.Menu.About"), Localization.Get("Window.About.ProjectHomeFailed"));
                    }
                }
                catch (Exception exception)
                {
                    error.IsVisible = true;
                    application.LogErrorOnce(Localization.Get("Workbench.Menu.About"),
                        Localization.Get("Window.About.ProjectHomeFailed"), exception);
                }
            };
            return CreateDialogSurface(new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Yohuke Danmu Neo", FontSize = 24, FontWeight = FontWeight.SemiBold,
                        TextWrapping = TextWrapping.Wrap
                    },
                    version, developer,
                    new TextBlock
                        { Text = "https://github.com/Yohuke-no-Symphony/DanmuNeo", TextWrapping = TextWrapping.Wrap },
                    link, error
                }
            });
        }, width: 560, sizing: WorkbenchAuxiliarySizing.ContentHeight);
    }

    internal void CheckForUpdates()
    {
        _ = application.CheckForUpdatesManuallyAsync();
        SyncDialogs();
        windows.FindAuxiliary("update")?.Activate();
    }

    internal void SaveLayoutAs()
    {
        windows.OpenLocalizedAuxiliary("save-layout", "Window.SaveLayout.Title", bindings =>
        {
            var name = new TextBox();
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
            var save = new Button { HorizontalAlignment = HorizontalAlignment.Right };
            bindings.Bind(name, TextBox.WatermarkProperty, "Layout.NamePlaceholder");
            bindings.Bind(save, ContentControl.ContentProperty, "Window.Save");
            save.Click += (_, _) =>
            {
                try
                {
                    layouts.SaveAs(name.Text?.Trim() ?? "");
                    windows.CloseAuxiliary("save-layout");
                }
                catch (ArgumentException exception)
                {
                    bindings.Bind(error, TextBlock.TextProperty, () => LayoutValidationFailure.GetDisplayMessage(exception));
                    error.IsVisible = true;
                    application.Log.Warning(Localization.Get("Workbench.Menu.Layouts"), exception.Message);
                }
            };
            return CreateDialogSurface(new StackPanel { Spacing = 16, Children = { name, error, save } });
        }, true, width: 480, sizing: WorkbenchAuxiliarySizing.ContentHeight);
    }

    internal void ManageLayouts()
    {
        windows.OpenLocalizedAuxiliary("manage-layouts", "Window.ManageLayouts.Title", bindings =>
        {
            var selection = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemTemplate = new FuncDataTemplate<WorkspaceLayoutPreset>((preset, _) =>
                    new TextBlock { Text = preset?.Name })
            };
            var name = new TextBox();
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var rename = new Button();
            var delete = new Button();
            var heading = new TextBlock { FontSize = 20 };
            bindings.Bind(name, TextBox.WatermarkProperty, "Layout.NewNamePlaceholder");
            bindings.Bind(rename, ContentControl.ContentProperty, "Layout.Rename");
            bindings.Bind(delete, ContentControl.ContentProperty, "Window.Delete");
            bindings.Bind(heading, TextBlock.TextProperty, "Layout.CustomLayouts");

            void Refresh()
            {
                var selectedId = (selection.SelectedItem as WorkspaceLayoutPreset)?.Id;
                var presets = layouts.CaptureFile().UserPresets;
                selection.ItemsSource = presets;
                selection.SelectedItem = presets.FirstOrDefault(preset => preset.Id == selectedId) ??
                                         presets.FirstOrDefault();
                rename.IsEnabled = delete.IsEnabled = presets.Count > 0;
            }

            selection.SelectionChanged += (_, _) => name.Text = (selection.SelectedItem as WorkspaceLayoutPreset)?.Name;
            rename.Click += (_, _) =>
            {
                if (selection.SelectedItem is not WorkspaceLayoutPreset preset)
                {
                    return;
                }

                try
                {
                    layouts.Rename(preset.Id, name.Text?.Trim() ?? "");
                    bindings.Bind(error, TextBlock.TextProperty, () => string.Empty);
                    Refresh();
                }
                catch (ArgumentException exception)
                {
                    bindings.Bind(error, TextBlock.TextProperty, () => LayoutValidationFailure.GetDisplayMessage(exception));
                    application.Log.Warning(Localization.Get("Workbench.Menu.Layouts"), exception.Message);
                }
            };
            delete.Click += (_, _) =>
            {
                if (selection.SelectedItem is not WorkspaceLayoutPreset preset)
                {
                    return;
                }

                windows.OpenLocalizedAuxiliary("delete-layout", "Window.DeleteLayout.Title", confirmationBindings =>
                    {
                        var confirm = new Button();
                        var cancel = new Button();
                        var question = new TextBlock { TextWrapping = TextWrapping.Wrap };
                        confirmationBindings.Bind(confirm, ContentControl.ContentProperty, "Window.Delete");
                        confirmationBindings.Bind(cancel, ContentControl.ContentProperty, "Window.Cancel");
                        confirmationBindings.Bind(question, TextBlock.TextProperty,
                            () => Localization.Format("Layout.DeleteQuestion", preset.Name));
                        cancel.Click += (_, _) => windows.CloseAuxiliary("delete-layout");
                        confirm.Click += (_, _) =>
                        {
                            layouts.Delete(preset.Id);
                            windows.CloseAuxiliary("delete-layout");
                            Refresh();
                        };
                        return CreateDialogSurface(new StackPanel
                        {
                            Spacing = 20,
                            Children =
                            {
                                question,
                                new StackPanel
                                {
                                    Orientation = Orientation.Horizontal, Spacing = 12, Children = { cancel, confirm }
                                }
                            }
                        });
                    }, true, owner: windows.FindAuxiliary("manage-layouts"), width: 440,
                    sizing: WorkbenchAuxiliarySizing.ContentHeight);
            };
            Refresh();
            return CreateDialogSurface(new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    heading, selection, name, error,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { rename, delete } }
                }
            });
        }, true, width: 540, height: 350);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        disposed = true;
        application.PropertyChanged -= OnApplicationChanged;
    }

    private static Border CreateDialogSurface(Control content)
    {
        var surface = new Border { Name = "DialogSurface", Padding = new(24), Child = content };
        surface.Bind(Border.BackgroundProperty, new DynamicResourceExtension("AppCardBrush"));
        return surface;
    }

    private void OnApplicationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IsSettingsOpen) or
            nameof(MainWindowViewModel.IsWorkspaceSettingsOpen) or
            nameof(MainWindowViewModel.IsAddDialogOpen) or nameof(MainWindowViewModel.IsConfirmDeleteDialogOpen) or
            nameof(MainWindowViewModel.IsUpdateDialogOpen))
        {
            SyncDialogs();
        }
    }

    private void SyncDialogs()
    {
        if (disposed)
        {
            return;
        }

        if (application.IsSettingsOpen)
        {
            windows.OpenLocalizedAuxiliary("settings", "Window.Settings.Title", _ =>
                {
                    var view = new SettingsDialogView { DataContext = application };
                    view.CloseRequested += (_, _) => windows.CloseAuxiliary("settings");
                    return view;
                }, true,
                () => application.IsSettingsOpen ? application.CloseSettingsAsync() : Task.CompletedTask,
                width: 1000, height: 740);
        }
        else
        {
            windows.CloseAuxiliary("settings");
        }

        if (application.IsWorkspaceSettingsOpen)
        {
            windows.OpenLocalizedAuxiliary("workspace-settings", "Window.WorkspaceSettings.Title", _ =>
                {
                    var view = new WorkspaceSettingsDialogView { DataContext = application };
                    view.CloseRequested += (_, _) => windows.CloseAuxiliary("workspace-settings");
                    return view;
                }, true,
                () => application.IsWorkspaceSettingsOpen ? application.CloseWorkspaceSettingsAsync() : Task.CompletedTask,
                width: 800, height: 740);
        }
        else
        {
            windows.CloseAuxiliary("workspace-settings");
        }

        if (application.IsAddDialogOpen)
        {
            windows.OpenLocalizedAuxiliary("add", "Window.Add.Title", _ => new AddWorkspaceDialogView { DataContext = application },
                true, () =>
                {
                    application.CloseAddDialog();
                    return Task.CompletedTask;
                }, width: 760,
                sizing: WorkbenchAuxiliarySizing.ContentHeight);
        }
        else
        {
            windows.CloseAuxiliary("add");
        }

        if (application.IsConfirmDeleteDialogOpen)
        {
            windows.OpenLocalizedAuxiliary("confirm", "Window.ConfirmDelete.Title", _ => new ConfirmDeleteDialogView { DataContext = application },
                true, () =>
                {
                    application.CloseDeleteDialog();
                    return Task.CompletedTask;
                },
                windows.CurrentDialogOwner, 600, sizing: WorkbenchAuxiliarySizing.ContentHeight);
        }
        else
        {
            windows.CloseAuxiliary("confirm");
        }

        if (application.IsUpdateDialogOpen)
        {
            windows.OpenLocalizedAuxiliary("update", "Window.CheckUpdates.Title", _ => new AppUpdateDialogView { DataContext = application },
                false, () =>
                {
                    application.CloseUpdateDialog();
                    return Task.CompletedTask;
                }, width: 620,
                sizing: WorkbenchAuxiliarySizing.ContentHeight);
        }
        else
        {
            windows.CloseAuxiliary("update");
        }
    }
}
