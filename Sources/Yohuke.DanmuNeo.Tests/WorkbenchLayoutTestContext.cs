using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Dock.Avalonia.Themes.Fluent;
using Yohuke.DanmuNeo.Layouts;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class WorkbenchLayoutTestContext : IAsyncDisposable
{
    internal WorkbenchLayoutTestContext(WorkspaceLayoutFile? initial = null, Action<string, string>? replaceFile = null)
    {
        if (!Application.Current!.Styles.OfType<DockFluentTheme>().Any())
        {
            Application.Current.Styles.Add(new DockFluentTheme());
        }

        DirectoryPath = Path.Combine(Path.GetTempPath(), "danmu-dock-" + Guid.NewGuid().ToString("N"));
        Owner = new() { Width = 1200, Height = 800 };
        Views = WorkbenchPanelIds.All.ToDictionary(id => id, id => (Control)new TextBox { Text = id });
        Store = new(DirectoryPath, replaceFile: replaceFile);
        Controller = new(Owner, Views, initial ?? WorkspaceLayoutPresets.CreateDefaultFile(), Store,
            RegisteredWindows.Add, window => UnregisteredWindows.Add(window));
        Owner.Content = Controller.Host;
    }

    internal string DirectoryPath { get; }
    internal Window Owner { get; }
    internal Dictionary<string, Control> Views { get; }
    internal WorkspaceLayoutStore Store { get; }
    internal WorkbenchLayoutController Controller { get; }
    internal List<Window> RegisteredWindows { get; } = [];
    internal List<Window> UnregisteredWindows { get; } = [];

    internal void Show()
    {
        Owner.Show();
        Pump();
    }

    internal void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        Owner.UpdateLayout();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await Controller.DisposeAsync();
        }
        finally
        {
            foreach (var window in RegisteredWindows)
            {
                window.Close();
            }

            Owner.Content = null;
            Owner.Close();
            Dispatcher.UIThread.RunJobs();

            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, true);
            }
        }
    }
}
