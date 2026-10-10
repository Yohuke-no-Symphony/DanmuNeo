using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit.Abstractions;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 使用正式样式验证中文行高与表单控件的垂直布局。
/// </summary>
public class TypographyViewTests
{
    private readonly ITestOutputHelper output;

    /// <summary>
    /// 创建带字体指标诊断输出的界面回归测试。
    /// </summary>
    public TypographyViewTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    /// <summary>
    /// 两种模式的中文文字均保留实际字体需要的行高，避免压缩行盒后基线偏移。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChineseFeedTextPreservesNaturalFontMetrics(bool compact)
    {
        var panel = new StackPanel();
        var grid = new Grid { Children = { panel } };
        grid.Classes.Set("compactDanmu", compact);

        foreach (var className in new[] { "danmuFeedUser", "danmuMedalText", "danmuFeedMeta", "danmuFeedContent" })
        {
            var text = new TextBlock { Text = "中文居中 Abc 123" };
            text.Classes.Add(className);
            panel.Children.Add(text);
        }

        var window = new Window { Width = 500, Height = 300, Content = grid };

        try
        {
            ShowWindow(window);

            foreach (var text in panel.Children.OfType<TextBlock>())
            {
                var line = text.TextLayout.TextLines[0];
                var naturalHeight = line.TextRuns.OfType<ShapedTextRun>().Max(run =>
                {
                    var metrics = run.GlyphRun.GlyphTypeface.Metrics;
                    var height = (metrics.Descent - metrics.Ascent + metrics.LineGap) * text.FontSize /
                                 metrics.DesignEmHeight;
                    output.WriteLine(
                        $"{string.Join(' ', text.Classes)}: {run.GlyphRun.GlyphTypeface.FamilyName}, font={text.FontSize}, line={line.Height}, natural={height}");
                    return height;
                });

                Assert.True(line.Height >= naturalHeight - 0.01,
                    $"{string.Join(' ', text.Classes)} 的行高 {line.Height} 小于字体自然行高 {naturalHeight}");
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 表单标签和单行输入垂直居中，多行输入保持从顶部开始编辑。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FormLabelAndSingleLineInputAreCentered(bool dark)
    {
        var label = new TextBlock { Text = "中文标签" };
        var input = new TextBox { Text = "中文输入 Abc" };
        var row = new Grid { ColumnDefinitions = new("Auto,*"), Height = 42, ColumnSpacing = 12 };
        Grid.SetColumn(input, 1);
        row.Children.Add(label);
        row.Children.Add(input);
        var multiline = new TextBox { Text = "中文第一行\n第二行", AcceptsReturn = true, Height = 80 };
        var button = new Button { Content = "开始监听", Width = 140, Height = 40 };
        var combo = new ComboBox { ItemsSource = new[] { "默认样式", "方括号" }, SelectedIndex = 0, Width = 180, Height = 40 };
        var numeric = new NumericUpDown { Value = 25, Width = 120, Height = 40 };
        var panel = new StackPanel
        {
            Margin = new(20), Spacing = 12,
            Children =
            {
                new TextBlock { Text = "中文文字对齐检查", Classes = { "sectionTitle" } },
                row,
                new StackPanel
                    { Orientation = Orientation.Horizontal, Spacing = 12, Children = { button, combo, numeric } },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 20,
                    Children =
                    {
                        new CheckBox { Content = "显示粉丝牌", IsChecked = true },
                        new RadioButton { Content = "跟随系统", IsChecked = true },
                        new ToggleButton { Content = "紧凑模式", IsChecked = true }
                    }
                },
                new TabControl
                {
                    Height = 74,
                    Items =
                    {
                        new TabItem { Header = "弹幕与同传", Content = new TextBlock { Text = "中日文、English、数字 12345 混排" } },
                        new TabItem { Header = "工作区设置" }
                    }
                },
                multiline
            }
        };
        var window = new Window
        {
            Width = 540, Height = 400, Content = panel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };

        try
        {
            ShowWindow(window);
            var labelCenter = label.TranslatePoint(new Point(0, label.TextLayout.Height / 2), row)!.Value.Y;
            Assert.InRange(labelCenter, row.Bounds.Height / 2 - 1, row.Bounds.Height / 2 + 1);
            var presenter = input.GetVisualDescendants().OfType<TextPresenter>().Single();
            Assert.Equal(label.FontFamily, input.FontFamily);
            Assert.Equal(label.FontFamily, presenter.FontFamily);
            Assert.Equal(label.FontFamily, button.FontFamily);
            Assert.Equal(label.FontFamily, combo.FontFamily);
            var inputCenter = presenter.TranslatePoint(new Point(0, presenter.TextLayout.Height / 2), row)!.Value.Y;
            Assert.InRange(inputCenter, row.Bounds.Height / 2 - 1, row.Bounds.Height / 2 + 1);
            var multilinePresenter = multiline.GetVisualDescendants().OfType<TextPresenter>().Single();
            Assert.True(multilinePresenter.TranslatePoint(default, multiline)!.Value.Y < 10);
            var outputDirectory = Environment.GetEnvironmentVariable("YOHUKE_FEED_QA_OUTPUT");

            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(outputDirectory, dark ? "typography-dark.png" : "typography-light.png"));
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 弹出菜单的中文与主窗口使用相同字体，避免回退到不同的拉丁字体基线。
    /// </summary>
    [AvaloniaFact]
    public void PopupMenuUsesSameFontAsMainWindow()
    {
        var button = new Button { Content = "打开菜单" };
        var window = new Window { Width = 300, Height = 160, Content = button };
        var item = new MenuItem { Header = "复制中文用户名" };
        var menu = new ContextMenu { Items = { item } };

        try
        {
            ShowWindow(window);
            menu.Open(button);
            Dispatcher.UIThread.RunJobs();
            var text = item.GetVisualDescendants().OfType<TextBlock>().Single(control => control.Text is "复制中文用户名");
            Assert.Equal(button.FontFamily, item.FontFamily);
            Assert.Equal(button.FontFamily, text.FontFamily);
            Assert.True(text.IsEffectivelyVisible);
        }
        finally
        {
            menu.Close();
            window.Close();
        }
    }

    private static void ShowWindow(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
