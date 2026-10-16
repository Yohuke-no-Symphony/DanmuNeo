using Avalonia.Controls;
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

    /// <summary>
    /// 初始化固定标题栏、菜单和房间工具条。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                Width = AppStateService.ClampWindowWidth(viewModel.Settings.WindowWidth);
                Height = AppStateService.ClampWindowHeight(viewModel.Settings.WindowHeight);
            }
        };
        Opened += (_, _) => Initialization = InitializeWorkbenchAsync();
    }

    internal Task Initialization { get; private set; } = Task.CompletedTask;
    internal WorkbenchSession? Workbench { get; private set; }

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
                viewModel.Dispose();
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
                viewModel.StatusMessage = $"无法完成退出保存：{exception.Message}";
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
            viewModel.StatusMessage = $"无法恢复布局：{exception.Message}";
            DockHost.Content = new TextBlock
            {
                Text = viewModel.StatusMessage,
                Margin = new(24),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            };
        }
    }
}
