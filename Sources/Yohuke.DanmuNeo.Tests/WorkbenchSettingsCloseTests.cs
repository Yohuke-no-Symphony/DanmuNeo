using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证设置保存失败的统一关闭入口与恢复能力。
/// </summary>
public class WorkbenchSettingsCloseTests
{
    /// <summary>
    /// 页面按钮和原生关闭保存失败时保留设置及录制清理结果，恢复存储后可再次关闭。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveFailureKeepsSettingsOpenAndCanRetry(bool closeFromPage)
    {
        using var context = new WorkspaceSidebarTestContext(240);
        var main = new MainWindow { DataContext = context.ViewModel };
        var settingsPath = context.StateService.SettingsFilePath;

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.MenuCatalog.SettingsCommand.Execute(null);
            var settings = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("settings"));
            var view = Assert.IsType<SettingsDialogView>(settings.Content);
            var recording = context.ViewModel.ShortcutBindingGroups.SelectMany(group => group.Bindings).First();
            context.ViewModel.BeginRecordShortcut(recording);
            File.Delete(settingsPath);
            Directory.CreateDirectory(settingsPath);

            if (closeFromPage)
            {
                view.CloseSettingsCommand.Execute(null);
            }
            else
            {
                settings.Close();
            }

            var deadline = DateTime.UtcNow.AddSeconds(5);

            while (!settings.Title!.StartsWith("保存失败：", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.StartsWith("保存失败：", settings.Title);
            Assert.True(settings.IsVisible);
            Assert.True(view.IsEnabled);
            Assert.True(context.ViewModel.IsSettingsOpen);
            Assert.False(context.ViewModel.IsRecordingShortcut);
            Assert.True(workbench.Windows.IsModal);
            Directory.Delete(settingsPath);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            settings.Closed += (_, _) => closed.TrySetResult();
            view.CloseSettingsCommand.Execute(null);
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(context.ViewModel.IsSettingsOpen);
            Assert.False(workbench.Windows.IsModal);
            Assert.True(File.Exists(settingsPath));
        }
        finally
        {
            if (Directory.Exists(settingsPath))
            {
                Directory.Delete(settingsPath);
            }

            await main.CloseWorkbenchAsync();
        }
    }
}
