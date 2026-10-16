using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.ViewModels.Items;
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
    /// 默认及窄设置窗口中的主题卡片和发送选项均留在内容区内，换行后仍能切换主题。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1000, 2)]
    [InlineData(760, 3)]
    [InlineData(560, 3)]
    [InlineData(440, 3)]
    public async Task AppearanceCardsWrapWithoutHorizontalOverflow(double width, int expectedRows)
    {
        using var fixture = new WorkspaceSidebarTestContext(240);
        var previousTheme = Application.Current!.RequestedThemeVariant;
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
            var choices = global.FindControl<ItemsControl>("ThemeModeChoices")!;
            var cards = choices.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("themeCardButton")).ToArray();
            Assert.Equal(3, cards.Length);
            var bounds = cards.Select(card => new Rect(card.TranslatePoint(default, choices)!.Value, card.Bounds.Size))
                .ToArray();
            var rowPositions = bounds.Select(rect => Math.Round(rect.Center.Y)).Distinct().ToArray();
            var wrap = choices.GetVisualDescendants().OfType<WrapPanel>().Single();
            var diagnostics = $"settings.ClientSize={settings.ClientSize}; global.Bounds={global.Bounds}; " +
                              $"choices.Bounds={choices.Bounds}; choices.DesiredSize={choices.DesiredSize}; " +
                              $"wrap.Bounds={wrap.Bounds}; wrap.DesiredSize={wrap.DesiredSize}; " +
                              string.Join("; ", cards.Select((card, index) =>
                                  $"card[{index}].Bounds={bounds[index]}, DesiredSize={card.DesiredSize}, Width={card.Width}, MaxWidth={card.MaxWidth}"));
            Assert.True(expectedRows == rowPositions.Length,
                $"预期 {expectedRows} 行，实际 {rowPositions.Length} 行。{diagnostics}");

            for (var index = 0; index < cards.Length; index++)
            {
                var card = cards[index];
                var rect = bounds[index];
                Assert.True(rect.Width > 0 && rect.Height > 0);
                Assert.InRange(rect.Left, 0, choices.Bounds.Width);
                Assert.InRange(rect.Right, rect.Left + 1, choices.Bounds.Width + 0.5);
                Assert.InRange(rect.Bottom, rect.Top + 1, choices.Bounds.Height + 0.5);
                var windowBounds = new Rect(card.TranslatePoint(default, settings)!.Value, card.Bounds.Size);
                Assert.InRange(windowBounds.Left, 0, settings.ClientSize.Width);
                Assert.InRange(windowBounds.Right, windowBounds.Left + 1, settings.ClientSize.Width + 0.5);

                if (index > 0)
                {
                    var previous = bounds[index - 1];
                    Assert.True(rect.Left >= previous.Right + 15.5 || rect.Top >= previous.Bottom + 15.5,
                        "主题卡片同排与换行后均应保留间距，且不相互重叠。");
                }
            }

            var scroll = global.GetVisualAncestors().OfType<ScrollViewer>().First();
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5);
            AssertGlobalSendingOptionsFit(global, settings);
            var dark = cards.Single(card => card.DataContext is ThemeModeOption { Value: AppThemeMode.Dark });
            var select = Assert.IsAssignableFrom<IAsyncRelayCommand>(dark.Command);
            await select.ExecuteAsync(dark.CommandParameter);
            PumpLayout(settings);
            Assert.Equal(AppThemeMode.Dark, fixture.ViewModel.Settings.ThemeMode);
            Assert.Contains("selected", dark.Classes);
            Assert.All(cards.Where(card => !ReferenceEquals(card, dark)), card => Assert.DoesNotContain("selected", card.Classes));
        }
        finally
        {
            await main.CloseWorkbenchAsync();
            Application.Current!.RequestedThemeVariant = previousTheme;
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
