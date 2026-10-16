using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证更新设置、提示绑定、发布页跳转和附着窗口生命周期。
/// </summary>
public class AppUpdateViewTests
{
    /// <summary>
    /// 杂项开关通过实际绑定关闭检查，并沿用关闭设置的保存入口。
    /// </summary>
    [AvaloniaFact]
    public async Task SettingsTogglePersistsDisabledChoice()
    {
        using var context = new AppUpdateTestContext();
        context.ViewModel.SelectSettingsSection(
            context.ViewModel.SettingsSectionOptions.Single(option => option.Key == "misc"));
        var view = new MiscSettingsView();
        var window = new Window { Content = view, DataContext = context.ViewModel };

        try
        {
            ShowWindow(window);
            var toggle = view.FindControl<CheckBox>("CheckForUpdatesOnStartupCheckBox")!;
            Assert.True(toggle.IsEffectivelyVisible);
            Assert.True(toggle.IsChecked);

            toggle.IsChecked = false;
            await context.ViewModel.CloseSettingsAsync();

            Assert.False(context.ViewModel.CheckForUpdatesOnStartup);
            Assert.False(context.StateService.Load().Settings.CheckForUpdatesOnStartup);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 两种主题下展示对应版本，按钮只打开该发布页并关闭提示。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PromptDisplaysVersionAndOpensMatchingRelease(bool dark)
    {
        using var context = new AppUpdateTestContext();
        context.UpdateService.Response.SetResult(context.Update);
        await context.ViewModel.CheckForUpdatesOnStartupAsync();
        Uri? openedUri = null;
        var view = new AppUpdateDialogView(uri =>
        {
            openedUri = uri;
            return Task.FromResult(true);
        });
        var window = new Window
        {
            Width = 760, Height = 460, Content = view, DataContext = context.ViewModel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };

        try
        {
            ShowWindow(window);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "最新版本：v9.8.7");
            var directory = Environment.GetEnvironmentVariable("YOHUKE_UPDATE_QA_OUTPUT");

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(directory, $"update-dialog-{(dark ? "dark" : "light")}.png"));
            }

            var button = view.FindControl<Button>("OpenReleaseButton")!;
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(button.Command).ExecuteAsync(null);

            Assert.Equal(context.Update.ReleaseUri, openedUri);
            Assert.False(context.ViewModel.IsUpdateDialogOpen);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 浏览器拒绝或启动异常时保留提示，仍可通过稍后按钮关闭。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaunchFailureKeepsPromptDismissible(bool throws)
    {
        using var context = new AppUpdateTestContext();
        context.UpdateService.Response.SetResult(context.Update);
        await context.ViewModel.CheckForUpdatesOnStartupAsync();
        var view = new AppUpdateDialogView(_ => throws
            ? Task.FromException<bool>(new InvalidOperationException("launcher unavailable"))
            : Task.FromResult(false));
        var window = new Window { Width = 760, Height = 460, Content = view, DataContext = context.ViewModel };

        try
        {
            ShowWindow(window);
            var button = view.FindControl<Button>("OpenReleaseButton")!;
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(button.Command).ExecuteAsync(null);

            Assert.True(context.ViewModel.IsUpdateDialogOpen);
            Assert.True(view.FindControl<TextBlock>("LaunchErrorText")!.IsVisible);
            view.FindControl<Button>("DismissUpdateButton")!.Command!.Execute(null);
            Assert.False(context.ViewModel.IsUpdateDialogOpen);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 更新在最小化前后返回时均随主窗口恢复，直接关闭窗口时同步清除提示状态。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AttachedPromptFollowsOwnerAndSynchronizesClose(bool startsMinimized)
    {
        using var context = new AppUpdateTestContext();
        var anchor = new Grid();
        var owner = new Window { Width = 980, Height = 640, Content = anchor };
        using var coordinator = new AttachedDialogCoordinator(owner, anchor);
        coordinator.Attach(context.ViewModel);

        try
        {
            ShowWindow(owner);

            if (startsMinimized)
            {
                owner.WindowState = WindowState.Minimized;
            }

            context.UpdateService.Response.SetResult(context.Update);
            await context.ViewModel.CheckForUpdatesOnStartupAsync();

            if (startsMinimized)
            {
                Assert.Empty(owner.OwnedWindows);
                owner.WindowState = WindowState.Normal;
            }

            var dialog = Assert.Single(owner.OwnedWindows);
            Assert.IsType<AppUpdateDialogView>(dialog.Content);
            Assert.True(dialog.IsVisible);

            owner.WindowState = WindowState.Minimized;
            Assert.False(dialog.IsVisible);
            owner.WindowState = WindowState.Normal;
            Assert.True(dialog.IsVisible);

            dialog.Close();
            Assert.False(context.ViewModel.IsUpdateDialogOpen);
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            coordinator.Dispose();
            owner.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void ShowWindow(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}