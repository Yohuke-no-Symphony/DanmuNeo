using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 歌词列表布局和操作命中区域的回归测试。
/// </summary>
public class LyricViewLayoutTests
{
    /// <summary>
    /// 本地与在线列表在不同侧栏宽度、主题和界面缩放下的场景。
    /// </summary>
    public static IEnumerable<object[]> LayoutCases
    {
        get
        {
            foreach (var online in new[] { false, true })
            {
                foreach (var width in new[] { 300d, 360d, 480d })
                {
                    foreach (var dark in new[] { false, true })
                    {
                        foreach (var scale in new[] { 1d, 1.5d, 2d })
                        {
                            yield return [online, width, dark, scale];
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 垂直滚动条不覆盖操作按钮，滚动列表不移动搜索区。
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(LayoutCases))]
    public void ActionsRemainOutsideScrollBar(bool online, double width, bool dark, double scale)
    {
        using var context = new LyricViewTestContext();
        var view = CreateView(online, true);
        var window = CreateWindow(context, view, width, dark, scale);

        try
        {
            window.Show();
            UpdateLayout(window);
            var results = GetResults(view);
            var scrollBar = results.GetVisualDescendants().OfType<ScrollBar>()
                .Single(control => control.Name == "PART_VerticalScrollBar");
            Assert.True(scrollBar.IsVisible);
            Assert.True(results.Extent.Height > results.Viewport.Height);
            Assert.Equal(ScrollBarVisibility.Disabled, results.HorizontalScrollBarVisibility);
            Assert.True(results.Extent.Width <= results.Viewport.Width + 0.5);
            var scrollBarBounds = GetBounds(scrollBar, window);
            var buttons = results.GetVisualDescendants().OfType<Button>()
                .Where(control => control.Classes.Contains("iconButton")).ToList();
            Assert.NotEmpty(buttons);

            foreach (var button in buttons)
            {
                var bounds = GetBounds(button, window);
                Assert.True(bounds.Right <= scrollBarBounds.Left + 0.5,
                    $"操作按钮右边缘 {bounds.Right} 超过滚动条左边缘 {scrollBarBounds.Left}");
            }

            var action = buttons[0];
            var actionBounds = GetBounds(action, window);
            var clicked = false;
            action.Click += (_, args) =>
            {
                clicked = true;
                args.Handled = true;
            };
            var point = new Point(actionBounds.Right - scale, actionBounds.Center.Y);
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(clicked);

            var search = view.GetVisualDescendants().OfType<TextBox>().Single();
            var searchBounds = GetBounds(search, window);
            Assert.True(searchBounds.Width > 100 * scale);
            results.Offset = new(0, 120);
            UpdateLayout(window);
            Assert.True(results.Offset.Y > 0);
            Assert.Equal(searchBounds, GetBounds(search, window));
            SavePreview(window, $"{(online ? "online" : "local")}-{width}-{(dark ? "dark" : "light")}-{scale}");
            search.Focus();
            window.KeyTextInput("长标题测试");
            Assert.Equal("长标题测试", search.Text);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 空列表和单条结果不显示滚动条，也不保留多余的横向滚动范围。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public void ShortResultsHideScrollBar(bool online, int count)
    {
        using var context = new LyricViewTestContext(count);
        var view = CreateView(online, true);
        var window = CreateWindow(context, view, 300, false, 1);

        try
        {
            window.Show();
            UpdateLayout(window);
            var results = GetResults(view);
            var scrollBar = results.GetVisualDescendants().OfType<ScrollBar>()
                .Single(control => control.Name == "PART_VerticalScrollBar");
            Assert.False(scrollBar.IsVisible);
            Assert.True(results.Extent.Width <= results.Viewport.Width + 0.5);
            Assert.Equal(0, results.Offset.Y);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 输入保持可编辑，平台按钮切换来源，Enter 搜索遵守搜索中状态。
    /// </summary>
    [AvaloniaFact]
    public void OnlineSearchKeyboardAndProviderSelectionWork()
    {
        using var context = new LyricViewTestContext();
        var view = CreateView(true, true);
        var window = CreateWindow(context, view, 300, true, 1);

        try
        {
            window.Show();
            UpdateLayout(window);
            var providers = view.GetVisualDescendants().OfType<Button>()
                .Where(control => control.Classes.Contains("musicProviderButton")).ToList();
            Click(window, providers[1]);
            Assert.Equal("qq", context.ViewModel.SelectedMusicLyricSource);
            Assert.Contains("selected", providers[1].Classes);
            Click(window, providers[0]);
            Assert.Equal("wy", context.ViewModel.SelectedMusicLyricSource);
            Assert.Contains("selected", providers[0].Classes);

            var search = view.GetVisualDescendants().OfType<TextBox>().Single();
            var searchButton = view.GetVisualDescendants().OfType<Button>()
                .Single(control => control.Classes.Contains("lyricSearchButton"));
            search.Focus();
            context.ViewModel.IsMusicLyricSearching = true;
            UpdateLayout(window);
            Assert.False(searchButton.IsEnabled);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal(24, context.ViewModel.MusicLyricSearchResults.Count);
            context.ViewModel.IsMusicLyricSearching = false;
            UpdateLayout(window);
            Assert.True(searchButton.IsEnabled);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Empty(context.ViewModel.MusicLyricSearchResults);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 更多菜单显式传递所在行的条目，且隐藏修改不影响删除操作。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MoreMenuTargetsItsRow(bool showEdit)
    {
        using var context = new LyricViewTestContext();
        var view = new LyricLibraryListView
        {
            EnableResultScrolling = true,
            ShowEditButton = showEdit
        };
        var window = CreateWindow(context, view, 300, true, 1);
        ContextMenu? menu = null;
        LyricEditDialogWindow? dialog = null;
        using var subscription = MenuBase.OpenedEvent.AddClassHandler<ContextMenu>((opened, _) => menu = opened);
        using var windowSubscription = Window.WindowOpenedEvent.AddClassHandler<LyricEditDialogWindow>((opened, _) => dialog = opened);

        try
        {
            window.Show();
            UpdateLayout(window);
            var more = GetResults(view).GetVisualDescendants().OfType<Button>()
                .First(control => Equals(ToolTip.GetTip(control), "更多操作"));
            Click(window, more);
            Assert.NotNull(menu);
            Assert.True(menu.IsOpen);
            var items = menu.ItemsSource!.Cast<MenuItem>().ToList();
            var edit = items.Single(item => Equals(item.Header, "修改"));
            var delete = items.Single(item => Equals(item.Header, "删除"));
            Assert.Equal(showEdit, edit.IsVisible);
            Assert.Same(more.DataContext, edit.CommandParameter);
            Assert.Same(more.DataContext, delete.CommandParameter);
            Assert.True(delete.Command!.CanExecute(delete.CommandParameter));
            var item = Assert.IsType<LyricLibraryItem>(more.DataContext);
            menu.Close();

            if (showEdit)
            {
                var editTask = Assert.IsAssignableFrom<IAsyncRelayCommand>(edit.Command)
                    .ExecuteAsync(edit.CommandParameter);
                Assert.NotNull(dialog);
                UpdateLayout(dialog);
                var title = dialog.GetVisualDescendants().OfType<TextBox>()
                    .Single(control => Equals(control.Watermark, "歌曲名"));
                title.Text = "更新的歌名";
                var save = dialog.GetVisualDescendants().OfType<Button>()
                    .Single(control => Equals(control.Content, "保存"));
                Click(dialog, save);
                await editTask;
                Assert.Equal("更新的歌名", item.Title);
            }

            await Assert.IsAssignableFrom<IAsyncRelayCommand>(delete.Command)
                .ExecuteAsync(delete.CommandParameter);
            Assert.DoesNotContain(item, context.ViewModel.LyricLibrary);
            Assert.DoesNotContain(item, context.ViewModel.FilteredLyricLibrary);
        }
        finally
        {
            menu?.Close();
            dialog?.Close();
            window.Close();
        }
    }

    /// <summary>
    /// 独立歌词库默认显示本地歌词，调整宽度时保留标签选择并正确发出关闭请求。
    /// </summary>
    [AvaloniaFact]
    public void PickerKeepsTabSelectionWhileResizingAndRequestsClose()
    {
        using var context = new LyricViewTestContext();
        context.ViewModel.OpenLyricLibraryPicker();
        Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        using var workbench = new WorkbenchContext(context.ViewModel);
        using var view = new LyricLibraryPickerView(workbench);
        var closeRequests = 0;
        view.CloseRequested += (_, _) => closeRequests++;
        var window = new Window
        {
            Width = 480,
            Height = 700,
            DataContext = context.ViewModel,
            Content = view
        };

        try
        {
            window.Show();
            UpdateLayout(window);
            var tabs = view.GetVisualDescendants().OfType<TabControl>()
                .Single(control => control.Classes.Contains("lyricPickerTabs"));
            Assert.Equal(0, tabs.SelectedIndex);
            tabs.SelectedIndex = 1;
            UpdateLayout(window);
            var online = tabs.GetVisualDescendants().OfType<LyricOnlineImportView>().Single();
            var title = view.GetVisualDescendants().OfType<TextBlock>()
                .Single(control => control.Text == "歌词库");
            var titleBounds = GetBounds(title, window);
            GetResults(online).Offset = new(0, 120);
            UpdateLayout(window);
            Assert.Equal(titleBounds, GetBounds(title, window));
            context.ViewModel.CloseLyricLibraryPicker();
            Assert.Equal(1, closeRequests);
            context.ViewModel.OpenLyricLibraryPicker();
            UpdateLayout(window);
            Assert.Equal(1, tabs.SelectedIndex);

            foreach (var width in new[] { 300d, 360d, 480d })
            {
                window.Width = width;
                UpdateLayout(window);
                var search = online.GetVisualDescendants().OfType<TextBox>().Single();
                Assert.True(search.Bounds.Width > 100);
                Assert.True(GetResults(online).Extent.Width <= GetResults(online).Viewport.Width + 0.5);
                Assert.Equal(1, tabs.SelectedIndex);
            }

            SavePreview(window, "sidebar-480-dark");
        }
        finally
        {
            window.DataContext = null;
            window.MouseUp(new(0, 0), MouseButton.Left);
            window.Close();
        }
    }

    /// <summary>
    /// 设置页使用外层滚动时，两个共享列表保持自然高度。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SettingsHostOwnsScrolling(bool online)
    {
        using var context = new LyricViewTestContext();
        var view = CreateView(online, false);
        var outer = new ScrollViewer
        {
            Content = new StackPanel { Children = { view } },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var window = CreateWindow(context, outer, 480, false, 1);

        try
        {
            window.Show();
            UpdateLayout(window);
            Assert.True(outer.Extent.Height > outer.Viewport.Height);
            Assert.Equal(ScrollBarVisibility.Disabled, GetResults(view).VerticalScrollBarVisibility);
            Assert.True(view.Bounds.Height > outer.Viewport.Height);
            outer.Offset = new(0, 100);
            UpdateLayout(window);
            Assert.True(outer.Offset.Y > 0);
        }
        finally
        {
            window.Close();
        }
    }

    private static UserControl CreateView(bool online, bool scrolling)
    {
        return online
            ? new LyricOnlineImportView
            {
                ShowHeader = false,
                ShowLocalImportButton = false,
                EnableResultScrolling = scrolling
            }
            : new LyricLibraryListView
            {
                EnableResultScrolling = scrolling
            };
    }

    private static Window CreateWindow(LyricViewTestContext context, Control content, double width, bool dark, double scale)
    {
        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        return new()
        {
            Width = width * scale,
            Height = 500 * scale,
            SystemDecorations = SystemDecorations.None,
            DataContext = context.ViewModel,
            Content = new LayoutTransformControl
            {
                LayoutTransform = new ScaleTransform(scale, scale),
                Child = new Border
                {
                    Classes = { "lyricPickerOverlay" },
                    Child = new Border
                    {
                        Classes = { "lyricPickerPanel" },
                        Child = content
                    }
                }
            }
        };
    }

    private static ScrollViewer GetResults(Control view)
    {
        return view.GetVisualDescendants().OfType<ScrollViewer>()
            .Single(control => control.Classes.Contains("musicLyricResults") ||
                               control.Classes.Contains("lyricLibraryResults"));
    }

    private static Rect GetBounds(Control control, Window window)
    {
        var transform = control.TransformToVisual(window)!.Value;
        return new Rect(control.Bounds.Size).TransformToAABB(transform);
    }

    private static void UpdateLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Click(Window window, Control control)
    {
        var point = GetBounds(control, window).Center;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        UpdateLayout(window);
    }

    private static void SavePreview(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("YOHUKE_LYRIC_QA_OUTPUT");

        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        window.MouseMove(new(-1, -1));
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, $"{name}.png"));
    }
}
