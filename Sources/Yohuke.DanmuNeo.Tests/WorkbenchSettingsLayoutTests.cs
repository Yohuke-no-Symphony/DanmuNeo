using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 使用真实工作台设置窗口验证外观选项和发送选项的收缩、换行及选择行为。
/// </summary>
public class WorkbenchSettingsLayoutTests
{
    /// <summary>
    /// 默认及窄设置窗口中的配色下拉和发送选项均留在内容区内，选择配色后即时应用并保存。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1000)]
    [InlineData(760)]
    [InlineData(560)]
    [InlineData(440)]
    public async Task ThemeSelectorFitsWithoutHorizontalOverflowAndAppliesSelection(double width)
    {
        using var fixture = new WorkspaceSidebarTestContext(240);
        var application = Application.Current!;
        var previousTheme = application.RequestedThemeVariant;
        var main = new MainWindow { DataContext = fixture.ViewModel };

        try
        {
            main.Show();
            await main.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(main.Workbench);
            workbench.MenuCatalog.SettingsCommand.Execute(null);
            var settings = Assert.IsAssignableFrom<Window>(workbench.Windows.FindAuxiliary("settings"));
            settings.Width = width;
            PumpLayout(settings);
            var global = settings.GetVisualDescendants().OfType<GlobalSettingsView>().Single();
            var choices = global.FindControl<ComboBox>("ThemeModeChoice");
            Assert.NotNull(choices);
            Assert.Equal(3, choices.Items.Count);
            Assert.Same(fixture.ViewModel.SelectedThemeModeOption, choices.SelectedItem);
            var bounds = new Rect(choices.TranslatePoint(default, settings)!.Value, choices.Bounds.Size);
            var diagnostics = $"settings.ClientSize={settings.ClientSize}; global.Bounds={global.Bounds}; " +
                              $"choices.Bounds={bounds}; choices.DesiredSize={choices.DesiredSize}";
            Assert.True(bounds.Width > 0 && bounds.Height > 0, diagnostics);
            Assert.InRange(bounds.Left, 0, settings.ClientSize.Width);
            Assert.InRange(bounds.Right, bounds.Left + 1, settings.ClientSize.Width + 0.5);
            var language = global.FindControl<ComboBox>("InterfaceLanguageChoice")!;
            Assert.Equal(language.Bounds.Width, choices.Bounds.Width);
            var scroll = global.GetVisualAncestors().OfType<ScrollViewer>().First();
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5);
            AssertGlobalSendingOptionsFit(global, settings);

            foreach (var mode in new[] { AppThemeMode.Light, AppThemeMode.System, AppThemeMode.Dark })
            {
                var option = fixture.ViewModel.ThemeModeOptions.Single(item => item.Value == mode);
                choices.SelectedItem = option;
                PumpLayout(settings);
                Assert.Equal(mode, fixture.ViewModel.Settings.ThemeMode);
                Assert.Same(option, fixture.ViewModel.SelectedThemeModeOption);
                Assert.Same(option, choices.SelectedItem);
                Assert.Equal(mode switch
                {
                    AppThemeMode.Light => ThemeVariant.Light,
                    AppThemeMode.Dark => ThemeVariant.Dark,
                    _ => ThemeVariant.Default
                }, application.RequestedThemeVariant);
            }

            await fixture.ViewModel.CloseSettingsAsync();
            Assert.Equal(AppThemeMode.Dark, fixture.StateService.Load().Settings.ThemeMode);
        }
        finally
        {
            await main.CloseWorkbenchAsync();
            application.RequestedThemeVariant = previousTheme;
        }
    }

    private static void AssertGlobalSendingOptionsFit(GlobalSettingsView global, Window settings)
    {
        var options = global.FindControl<Panel>("GlobalSendingOptions")!;
        var checkBoxes = options.Children.OfType<CheckBox>().ToArray();
        Assert.Equal(3, checkBoxes.Length);
        var bounds = checkBoxes
            .Select(checkBox => new Rect(checkBox.TranslatePoint(default, options)!.Value, checkBox.Bounds.Size))
            .ToArray();
        var diagnostics = $"settings.ClientSize={settings.ClientSize}; options.Bounds={options.Bounds}; " +
                          string.Join("; ", bounds.Select((rect, index) => $"option[{index}]={rect}"));

        for (var index = 0; index < checkBoxes.Length; index++)
        {
            var checkBox = checkBoxes[index];
            var rect = bounds[index];
            Assert.True(rect.Width > 0 && rect.Height > 0, diagnostics);
            Assert.True(rect.Left >= 0 && rect.Right <= options.Bounds.Width + 0.5, diagnostics);
            Assert.True(rect.Top >= 0 && rect.Bottom <= options.Bounds.Height + 0.5, diagnostics);

            foreach (var text in checkBox.GetVisualDescendants().OfType<TextBlock>())
            {
                var textBounds = new Rect(text.TranslatePoint(default, options)!.Value, text.Bounds.Size);
                Assert.True(textBounds.Left >= 0 && textBounds.Right <= options.Bounds.Width + 0.5, diagnostics);
                Assert.True(textBounds.Top >= 0 && textBounds.Bottom <= options.Bounds.Height + 0.5, diagnostics);
            }

            if (index > 0)
            {
                var previous = bounds[index - 1];
                Assert.True(rect.Left >= previous.Right + 15.5 || rect.Top >= previous.Bottom + 7.5,
                    $"发送选项换行后仍须保留间距。{diagnostics}");
            }
        }
    }

    private static void PumpLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
