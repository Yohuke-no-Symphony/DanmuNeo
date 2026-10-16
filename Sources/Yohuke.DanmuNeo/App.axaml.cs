using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.Views;

namespace Yohuke.DanmuNeo;

public partial class App : Application
{
    public override void Initialize()
    {
        StartupLog.Append("App.Initialize begin");
        AvaloniaXamlLoader.Load(this);
        StartupLog.Append("App.Initialize end");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        StartupLog.Append($"OnFrameworkInitializationCompleted lifetime={ApplicationLifetime?.GetType().FullName}");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            StartupLog.Append("Create MainWindow begin");
            var viewModel = new MainWindowViewModel();
            var mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
            AppThemeService.Apply(viewModel.Settings.ThemeMode);
            StartupLog.Append("Create MainWindow end");

            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            mainWindow.Activate();
            StartupLog.Append($"MainWindow visible={mainWindow.IsVisible} state={mainWindow.WindowState}");
            _ = viewModel.CheckForUpdatesOnStartupAsync();
        }

        base.OnFrameworkInitializationCompleted();
        StartupLog.Append("OnFrameworkInitializationCompleted end");
    }
}
