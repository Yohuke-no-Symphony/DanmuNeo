using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 显示可用更新并打开 GitHub 发布页面。
/// </summary>
public partial class AppUpdateDialogView : UserControl
{
    private readonly Func<Uri, Task<bool>> openRelease;
    private readonly TextBlock launchErrorText;
    private readonly TextBlock latestVersionText;
    private readonly TextBlock currentVersionText;
    private MainWindowViewModel? observedViewModel;
    private bool isAttached;

    /// <summary>
    /// 初始化更新提示对话框。
    /// </summary>
    public AppUpdateDialogView()
        : this(null)
    {
    }

    internal AppUpdateDialogView(Func<Uri, Task<bool>>? openRelease)
    {
        this.openRelease = openRelease ?? (uri => TopLevel.GetTopLevel(this)!.Launcher.LaunchUriAsync(uri));
        AvaloniaXamlLoader.Load(this);
        launchErrorText = this.GetControl<TextBlock>("LaunchErrorText");
        latestVersionText = this.GetControl<TextBlock>("LatestVersionText");
        currentVersionText = this.GetControl<TextBlock>("CurrentVersionText");
        DataContextChanged += (_, _) =>
        {
            ObserveViewModel(isAttached ? DataContext as MainWindowViewModel : null);
            RefreshVersions();
        };
        RefreshVersions();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        isAttached = true;
        Localization.LanguageChanged += OnLanguageChanged;
        ObserveViewModel(DataContext as MainWindowViewModel);
        RefreshVersions();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        isAttached = false;
        Localization.LanguageChanged -= OnLanguageChanged;
        ObserveViewModel(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void ObserveViewModel(MainWindowViewModel? nextViewModel)
    {
        if (observedViewModel is not null)
        {
            observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        observedViewModel = nextViewModel;

        if (observedViewModel is not null)
        {
            observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.AvailableUpdate) or nameof(MainWindowViewModel.AppVersion))
        {
            RefreshVersions();
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshVersions();
    }

    private void RefreshVersions()
    {
        var viewModel = DataContext as MainWindowViewModel;
        latestVersionText.Text = Localization.Format("UI.AppUpdateDialogView.LatestVersion", viewModel?.AvailableUpdate?.TagName ?? "");
        currentVersionText.Text = Localization.Format("UI.AppUpdateDialogView.CurrentVersion", viewModel?.AppVersion ?? "");
    }

    [RelayCommand]
    private async Task OpenReleaseAsync()
    {
        if (DataContext is not MainWindowViewModel { AvailableUpdate: { } update } viewModel)
        {
            return;
        }

        launchErrorText.IsVisible = false;

        try
        {
            if (await openRelease(update.ReleaseUri))
            {
                viewModel.CloseUpdateDialog();
                return;
            }

            viewModel.Log.Error(Localization.Get("UI.AppUpdateDialogView.LogSource"), Localization.Get("UI.AppUpdateDialogView.ReleasePageLaunchFailed"));
        }
        catch (Exception exception)
        {
            viewModel.LogErrorOnce(Localization.Get("UI.AppUpdateDialogView.LogSource"), Localization.Get("UI.AppUpdateDialogView.ReleasePageLaunchFailed"), exception);
        }

        launchErrorText.IsVisible = true;
    }
}
