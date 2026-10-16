using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
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
        windows.OpenAuxiliary("about", "关于 Yohuke Danmu Neo", () =>
        {
            var link = new Button { Content = "项目主页" };
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            link.Click += async (_, _) =>
            {
                try
                {
                    if (!await TopLevel.GetTopLevel(link)!.Launcher.LaunchUriAsync(
                            new("https://github.com/Yohuke-no-Symphony/DanmuNeo")))
                    {
                        error.Text = "无法打开浏览器。";
                    }
                }
                catch (Exception)
                {
                    error.Text = "无法打开浏览器。";
                }
            };
            return new StackPanel
            {
                Margin = new(24), Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "Yohuke Danmu Neo", FontSize = 24, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = $"版本 {application.AppVersion}" },
                    new TextBlock { Text = "开发者：Yohuke no Symphony" },
                    new TextBlock { Text = "https://github.com/Yohuke-no-Symphony/DanmuNeo", TextWrapping = TextWrapping.Wrap },
                    link, error
                }
            };
        }, width: 560, height: 330);
    }

    internal void CheckForUpdates()
    {
        _ = application.CheckForUpdatesManuallyAsync();
        SyncDialogs();
        windows.FindAuxiliary("update")?.Activate();
    }

    internal void SaveLayoutAs()
    {
        windows.OpenAuxiliary("save-layout", "另存为布局", () =>
        {
            var name = new TextBox { Watermark = "布局名称" };
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var save = new Button { Content = "保存", HorizontalAlignment = HorizontalAlignment.Right };
            save.Click += (_, _) =>
            {
                try
                {
                    layouts.SaveAs(name.Text?.Trim() ?? "");
                    windows.CloseAuxiliary("save-layout");
                }
                catch (ArgumentException exception)
                {
                    error.Text = exception.Message;
                }
            };
            return new StackPanel { Margin = new(24), Spacing = 16, Children = { name, error, save } };
        }, true, width: 480, height: 280);
    }

    internal void ManageLayouts()
    {
        windows.OpenAuxiliary("manage-layouts", "管理布局", () =>
        {
            var selection = new ComboBox
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemTemplate = new FuncDataTemplate<WorkspaceLayoutPreset>((preset, _) =>
                    new TextBlock { Text = preset?.Name })
            };
            var name = new TextBox { Watermark = "新名称" };
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var rename = new Button { Content = "重命名" };
            var delete = new Button { Content = "删除" };

            void Refresh()
            {
                var selectedId = (selection.SelectedItem as WorkspaceLayoutPreset)?.Id;
                var presets = layouts.CaptureFile().UserPresets;
                selection.ItemsSource = presets;
                selection.SelectedItem = presets.FirstOrDefault(preset => preset.Id == selectedId) ?? presets.FirstOrDefault();
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
                    error.Text = "";
                    Refresh();
                }
                catch (ArgumentException exception)
                {
                    error.Text = exception.Message;
                }
            };
            delete.Click += (_, _) =>
            {
                if (selection.SelectedItem is not WorkspaceLayoutPreset preset)
                {
                    return;
                }

                windows.OpenAuxiliary("delete-layout", "删除布局", () =>
                {
                    var confirm = new Button { Content = "删除" };
                    var cancel = new Button { Content = "取消" };
                    cancel.Click += (_, _) => windows.CloseAuxiliary("delete-layout");
                    confirm.Click += (_, _) =>
                    {
                        layouts.Delete(preset.Id);
                        windows.CloseAuxiliary("delete-layout");
                        Refresh();
                    };
                    return new StackPanel
                    {
                        Margin = new(24), Spacing = 20,
                        Children =
                        {
                            new TextBlock { Text = $"删除布局“{preset.Name}”？", TextWrapping = TextWrapping.Wrap },
                            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { cancel, confirm } }
                        }
                    };
                }, true, owner: windows.FindAuxiliary("manage-layouts"), width: 440, height: 240);
            };
            Refresh();
            return new StackPanel
            {
                Margin = new(24), Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "自定义布局", FontSize = 20 }, selection, name, error,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { rename, delete } }
                }
            };
        }, true, width: 540, height: 350);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        disposed = true;
        application.PropertyChanged -= OnApplicationChanged;
    }

    private void OnApplicationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IsSettingsOpen) or
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
            windows.OpenAuxiliary("settings", "设置", () =>
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

        if (application.IsAddDialogOpen)
        {
            windows.OpenAuxiliary("add", "添加工作区或直播间", () => new AddWorkspaceDialogView { DataContext = application },
                true, () => { application.CloseAddDialog(); return Task.CompletedTask; }, width: 760, height: 680);
        }
        else
        {
            windows.CloseAuxiliary("add");
        }

        if (application.IsConfirmDeleteDialogOpen)
        {
            windows.OpenAuxiliary("confirm", "确认删除", () => new ConfirmDeleteDialogView { DataContext = application },
                true, () => { application.CloseDeleteDialog(); return Task.CompletedTask; },
                windows.ActiveWindow, 600, 360);
        }
        else
        {
            windows.CloseAuxiliary("confirm");
        }

        if (application.IsUpdateDialogOpen)
        {
            windows.OpenAuxiliary("update", "检查更新", () => new AppUpdateDialogView { DataContext = application },
                false, () => { application.CloseUpdateDialog(); return Task.CompletedTask; }, width: 620, height: 380);
        }
        else
        {
            windows.CloseAuxiliary("update");
        }
    }
}
