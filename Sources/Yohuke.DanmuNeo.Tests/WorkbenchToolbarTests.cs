using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证真实工作台按钮的可用内容区域和工具条重排，避免容器尺寸正确而图标被裁切。
/// </summary>
public class WorkbenchToolbarTests
{
    /// <summary>
    /// 主窗与浮窗的图标按钮必须保留完整图标空间，不能继承文字按钮的内边距。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IconButtonsKeepUsableContentInMainAndFloatingWindows(bool dark)
    {
        using var fixture = new WorkspaceSidebarTestContext(240);
        var window = new MainWindow
        {
            DataContext = fixture.ViewModel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };

        try
        {
            window.Show();
            await window.Initialization;
            var workbench = Assert.IsType<WorkbenchSession>(window.Workbench);
            workbench.Layouts.ApplyPreset(WorkspaceLayoutPresets.LYRICS);
            workbench.Layouts.FloatPanel(WorkbenchPanelIds.LIVE_PLAYER);
            var floating = Assert.Single(workbench.Layouts.FloatingWindows);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            floating.UpdateLayout();
            var buttons = window.GetVisualDescendants().Concat(floating.GetVisualDescendants())
                .OfType<Button>().Where(button => button.Classes.Contains("iconButton") && button.IsEffectivelyVisible).ToArray();
            Assert.True(buttons.Length >= 10);

            foreach (var button in buttons)
            {
                var availableWidth = button.Bounds.Width - button.Padding.Left - button.Padding.Right -
                                     button.BorderThickness.Left - button.BorderThickness.Right;
                var availableHeight = button.Bounds.Height - button.Padding.Top - button.Padding.Bottom -
                                      button.BorderThickness.Top - button.BorderThickness.Bottom;
                Assert.True(availableWidth >= 16, $"{button.Name}: 图标区域宽度 {availableWidth}，按钮 {button.Bounds}，Padding {button.Padding}");
                Assert.True(availableHeight >= 16, $"{button.Name}: 图标区域高度 {availableHeight}");
                Assert.Equal(button.Bounds.Width, button.Bounds.Height);
            }
        }
        finally
        {
            await window.CloseWorkbenchAsync();
        }
    }

    /// <summary>
    /// 播放工具条在窄面板中换行后仍保留按钮间距，质量选择与音量控制不越界。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(240)]
    [InlineData(360)]
    [InlineData(640)]
    public void PlayerToolbarKeepsSpacingAndFitsNarrowPanels(double width)
    {
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var player = new LivePlayerPanelView(context);
        var window = new Window { Width = width, Height = 320, Content = player };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var buttons = player.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("iconButton")).ToArray();
            Assert.Equal(3, buttons.Length);

            for (var index = 1; index < buttons.Length; index++)
            {
                var previous = buttons[index - 1];
                var previousRight = previous.TranslatePoint(new Point(previous.Bounds.Width, 0), player)!.Value;
                var currentLeft = buttons[index].TranslatePoint(default, player)!.Value;
                Assert.True(currentLeft.Y > previousRight.Y || currentLeft.X - previousRight.X >= 8,
                    $"播放按钮之间缺少间距：{previousRight} → {currentLeft}");
            }

            var controls = buttons.Cast<Control>()
                .Concat(player.GetVisualDescendants().OfType<ComboBox>())
                .Concat(player.GetVisualDescendants().OfType<Slider>());

            foreach (var control in controls)
            {
                var origin = control.TranslatePoint(default, player)!.Value;
                Assert.InRange(origin.X, 0, player.Bounds.Width);
                Assert.InRange(origin.X + control.Bounds.Width, 0, player.Bounds.Width);
            }

            var slider = player.GetVisualDescendants().OfType<Slider>().Single();
            var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
            var sliderCenter = slider.TranslatePoint(new Point(0, slider.Bounds.Height / 2), player)!.Value.Y;
            var thumbCenter = thumb.TranslatePoint(new Point(0, thumb.Bounds.Height / 2), player)!.Value.Y;
            Assert.InRange(Math.Abs(sliderCenter - thumbCenter), 0, 0.5);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 标题栏保留监听按钮的状态颜色、图标和原命令。
    /// </summary>
    [AvaloniaFact]
    public async Task RoomToolbarPreservesListeningStatePresentation()
    {
        using var fixture = new WorkspaceSidebarTestContext(240);
        var window = new MainWindow { DataContext = fixture.ViewModel };

        try
        {
            window.Show();
            await window.Initialization;
            var room = fixture.ViewModel.SelectedWorkspace!.SelectedRoom!;
            var title = window.GetVisualDescendants().OfType<TitleBarView>().Single();
            var button = title.FindControl<Button>("ListenToggleButton")!;
            Assert.Same(room.ToggleListeningCommand, button.Command);
            Assert.Contains("listenToggleButton", button.Classes);

            foreach (var listening in new[] { false, true, false })
            {
                room.IsListening = listening;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(listening, button.Classes.Contains("listening"));
                Assert.Equal(!listening, button.Classes.Contains("idle"));
                Assert.Equal(Color.Parse(listening ? "#22C55E" : "#EF4444"),
                    Assert.IsAssignableFrom<ISolidColorBrush>(button.BorderBrush).Color);
                var content = Assert.IsType<StackPanel>(button.Content);
                Assert.Equal(!listening, content.Children[0].IsVisible);
                Assert.Equal(listening, content.Children[1].IsVisible);
                Assert.Equal(listening ? "停止" : "监听", Assert.IsType<TextBlock>(content.Children[2]).Text);
            }
        }
        finally
        {
            await window.CloseWorkbenchAsync();
        }
    }
}
