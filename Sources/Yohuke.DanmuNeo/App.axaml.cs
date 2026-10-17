using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.Views;

namespace Yohuke.DanmuNeo;

public partial class App : Application
{
    private readonly AppLogService log = new();
    private AppLogRuntimeBridge? logBridge;

    public override void Initialize()
    {
        logBridge = new(log);
        StartupLog.Append("App.Initialize begin");
        Localization.Initialize(Path.Combine(AppContext.BaseDirectory, "i18n"));
        AvaloniaXamlLoader.Load(this);

        if (OperatingSystem.IsMacOS() && NativeMenu.GetMenu(this) is null)
        {
            NativeMenu.SetMenu(this, new());
        }

        StartupLog.Append("App.Initialize end");
    }

    public override void OnFrameworkInitializationCompleted()
    {
        StartupLog.Append($"OnFrameworkInitializationCompleted lifetime={ApplicationLifetime?.GetType().FullName}");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            StartupLog.Append("Create MainWindow begin");
            desktop.Exit += OnExit;
            var stateService = new AppStateService();
            var state = stateService.Load();
            AppLanguageService.ApplyPreference(state.Settings.Language);
            var viewModel = new MainWindowViewModel(stateService, log: log, loadedState: state);
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

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (sender is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit -= OnExit;
        }

        logBridge?.Dispose();
        log.Dispose();
    }
}
