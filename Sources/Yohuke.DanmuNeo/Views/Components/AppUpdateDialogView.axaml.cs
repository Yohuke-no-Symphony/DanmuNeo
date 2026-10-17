using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Views.Components;

/// <summary>
/// 显示可用更新并打开 GitHub 发布页面。
/// </summary>
public partial class AppUpdateDialogView : UserControl
{
    private readonly Func<Uri, Task<bool>> openRelease;
    private readonly TextBlock launchErrorText;

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

            viewModel.Log.Error("更新", "无法打开更新下载页面");
        }
        catch (Exception exception)
        {
            viewModel.LogErrorOnce("更新", "无法打开更新下载页面", exception);
        }

        launchErrorText.IsVisible = true;
    }
}
