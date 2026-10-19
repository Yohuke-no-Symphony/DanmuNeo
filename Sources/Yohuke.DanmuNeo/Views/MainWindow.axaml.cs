using Yohuke.DanmuNeo.I18n;
using Avalonia.Controls;
using Avalonia.Data;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Views;

/// <summary>
/// 组合全局工作台并协调有序退出的主窗口。
/// </summary>
public partial class MainWindow : Window
{
    private bool closing;
    private bool allowClose;
    private IWindowChrome? chrome;

    /// <summary>
    /// 初始化融合房间信息和菜单的固定标题栏。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
        Closed += (_, _) => chrome?.Dispose();
    }

    internal Task Initialization { get; private set; } = Task.CompletedTask;
    internal WorkbenchSession? Workbench { get; private set; }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var size = new Avalonia.Size(AppStateService.ClampWindowWidth(viewModel.Settings.WindowWidth),
            AppStateService.ClampWindowHeight(viewModel.Settings.WindowHeight));

        if (chrome is null)
        {
            Width = size.Width;
            Height = size.Height;
            chrome = WindowChrome.Attach(this, MainTitleBar);
        }
        else
        {
            chrome.ResizeClient(size);
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        chrome ??= WindowChrome.Attach(this, MainTitleBar);
        Initialization = InitializeWorkbenchAsync();
    }

    /// <inheritdoc/>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (allowClose)
        {
            return;
        }

        e.Cancel = true;

        if (!closing)
        {
            _ = CloseWorkbenchAsync();
        }
    }

    internal async Task CloseWorkbenchAsync()
    {
        if (closing)
        {
            return;
        }

        closing = true;

        try
        {
            await Initialization;
            var viewModel = Workbench?.Context.Application ?? DataContext as MainWindowViewModel;

            if (viewModel is not null && WindowState == WindowState.Normal)
            {
                viewModel.Settings.WindowWidth = AppStateService.ClampWindowWidth(Width);
                viewModel.Settings.WindowHeight = AppStateService.ClampWindowHeight(Height);
            }

            if (Workbench is not null)
            {
                Workbench.SetShutdownPending(true);
                await Workbench.SaveForShutdownAsync();
                await Workbench.DisposeAsync();
            }
            else if (viewModel is not null)
            {
                await viewModel.SaveAsync();
                await viewModel.DisposeAsync();
            }

            allowClose = true;
            Close();
        }
        catch (Exception exception)
        {
            closing = false;
            Workbench?.SetShutdownPending(false);
            var viewModel = Workbench?.Context.Application ?? DataContext as MainWindowViewModel;

            if (viewModel is not null)
            {
                viewModel.SetLocalizedStatus("UI.MainWindow.ShutdownSaveFailed", new Func<string>(() => LocalizedExceptionText.Get(exception)));
                viewModel.LogErrorOnce(Localization.Get("UI.MainWindow.ShutdownLogSource"), Localization.Get("UI.MainWindow.ShutdownLogFailed"), exception);
            }
        }
    }

    private async Task InitializeWorkbenchAsync()
    {
        if (Workbench is not null || DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        try
        {
            Workbench = await WorkbenchSession.CreateAsync(this, WindowMenu, viewModel);
            DockHost.Content = Workbench.Layouts.Host;
        }
        catch (Exception exception)
        {
            viewModel.SetLocalizedStatus("UI.MainWindow.LayoutRestoreFailed", new Func<string>(() => LocalizedExceptionText.Get(exception)));
            viewModel.LogErrorOnce(Localization.Get("UI.MainWindow.LayoutLogSource"), Localization.Get("UI.MainWindow.LayoutLogFailed"), exception);
            var errorText = new TextBlock
            {
                Margin = new(24),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };
            errorText.Bind(TextBlock.TextProperty,
                new Binding(nameof(MainWindowViewModel.StatusMessage)) { Source = viewModel });
            DockHost.Content = errorText;
        }
    }
}
