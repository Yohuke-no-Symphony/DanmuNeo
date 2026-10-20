using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Models.LiveMarker;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证记录面板的独立输入、真实表格编辑事务和迟到文件选择归属。
/// </summary>
public sealed class LiveMarkerPanelViewTests
{
    /// <summary>
    /// 真实绑定保持独立草稿和只读实际时间，Enter 只向记录列表添加。
    /// </summary>
    [AvaloniaFact]
    public async Task InputAndRealTimeVisibilityStayIndependentFromTranslationDraft()
    {
        var directory = CreateDirectory();
        using var fixture = new LiveRoomFeedViewTestContext();
        fixture.ViewModel.Settings.LiveMarkerAutoSaveEnabled = false;
        var marker = fixture.Room.LiveMarker;
        await OpenDocumentAsync(marker, Path.Combine(directory, "markers.yaml"));
        fixture.Room.InputDraft = "同传草稿";
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panel = new LiveMarkerPanelView(context);
        var window = ShowWindow(panel);

        try
        {
            var grid = panel.FindControl<DataGrid>("MarkerDataGrid")!;
            Assert.Same(fixture.Room, panel.DataContext);
            Assert.Same(marker, panel.FindControl<Grid>("MarkerContent")!.DataContext);
            Assert.Same(marker.Entries, grid.ItemsSource);
            Assert.Equal(4, grid.Columns.Count);
            Assert.True(grid.Columns[1].IsReadOnly);
            marker.ShowRealTimeColumn = false;
            Assert.False(grid.Columns[1].IsVisible);
            marker.ShowRealTimeColumn = true;
            Assert.True(grid.Columns[1].IsVisible);
            var input = panel.FindControl<TextBox>("MarkerInput")!;
            input.Text = "独立切片记录";
            panel.FocusInput();
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);

            Assert.Equal("独立切片记录", Assert.Single(marker.Entries).Snapshot.Message);
            Assert.Equal("", marker.DraftText);
            Assert.Equal("同传草稿", fixture.Room.InputDraft);
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Tunnel 快捷键不会抢走表格 Enter/Esc，也不会抢走文本框原生撤销和删除。
    /// </summary>
    [AvaloniaFact]
    public async Task ShortcutBoundaryProtectsGridAndTextEditing()
    {
        var directory = CreateDirectory();
        using var fixture = new LiveRoomFeedViewTestContext();
        fixture.ViewModel.Settings.LiveMarkerAutoSaveEnabled = false;
        var marker = fixture.Room.LiveMarker;
        await OpenDocumentAsync(marker, Path.Combine(directory, "markers.yaml"), WithEntry());
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panel = new LiveMarkerPanelView(context);
        var window = ShowWindow(panel);

        try
        {
            marker.DraftText = "尚未提交的记录";
            var grid = panel.FindControl<DataGrid>("MarkerDataGrid")!;
            grid.Focus();
            Pump(window);
            Assert.False(panel.TryHandleShortcut("liveMarker.add", false));
            Assert.False(panel.TryHandleShortcut("liveMarker.lockTime", false));
            Assert.False(panel.TryHandleShortcut("liveMarker.unlockTime", false));
            Assert.Single(marker.Entries);
            panel.FocusInput();
            Pump(window);
            Assert.False(panel.TryHandleShortcut("liveMarker.undo", true));
            Assert.False(panel.TryHandleShortcut("liveMarker.redo", true));
            Assert.False(panel.TryHandleShortcut("liveMarker.delete", true));
            Assert.True(panel.TryHandleShortcut("liveMarker.color4", true));
            Assert.Equal(LiveMarkerColor.Green, marker.SelectedColor);
            Assert.True(panel.TryHandleShortcut("liveMarker.unlockTime", true));
            Assert.False(marker.IsTimeLocked);
            Assert.True(panel.TryHandleShortcut("liveMarker.lockTime", true));
            Assert.True(marker.IsTimeLocked);
            Assert.True(panel.TryHandleShortcut("liveMarker.add", true));
            Assert.Equal(2, marker.Entries.Count);
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// 输入法预编辑中的 Enter 不提交打点，完成预编辑后正常添加。
    /// </summary>
    [AvaloniaFact]
    public async Task CompositionEnterDoesNotCreateMarker()
    {
        var directory = CreateDirectory();
        using var fixture = new LiveRoomFeedViewTestContext();
        fixture.ViewModel.Settings.LiveMarkerAutoSaveEnabled = false;
        var marker = fixture.Room.LiveMarker;
        await OpenDocumentAsync(marker, Path.Combine(directory, "markers.yaml"));
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panel = new LiveMarkerPanelView(context);
        var window = ShowWindow(panel);

        try
        {
            var input = panel.FindControl<TextBox>("MarkerInput")!;
            input.Text = "已有记录";
            panel.FocusInput();
            Pump(window);
            var presenter = input.GetVisualDescendants().OfType<TextPresenter>().Single();
            presenter.PreeditText = "pinyin";
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Empty(marker.Entries);
            presenter.PreeditText = null;
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Single(marker.Entries);
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// 非法直播时间阻止表格提交，Esc 恢复；有效编辑可撤销且不改变实际时间。
    /// </summary>
    [AvaloniaFact]
    public async Task DataGridValidationCancellationAndCommitPreserveIndependentRealTime()
    {
        var directory = CreateDirectory();
        using var fixture = new LiveRoomFeedViewTestContext();
        fixture.ViewModel.Settings.LiveMarkerAutoSaveEnabled = false;
        var marker = fixture.Room.LiveMarker;
        var original = WithEntry();
        await OpenDocumentAsync(marker, Path.Combine(directory, "markers.yaml"), original);
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panel = new LiveMarkerPanelView(context);
        var window = ShowWindow(panel, 900);

        try
        {
            var grid = panel.FindControl<DataGrid>("MarkerDataGrid")!;
            var row = Assert.Single(marker.Entries);
            var input = BeginCellEdit(window, grid, row, 0);
            input.Text = "invalid";
            Pump(window);
            Assert.True(row.HasErrors);
            Assert.False(panel.CommitEditing());
            Assert.Equal(original.LiveTime, row.Snapshot.LiveTime);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);
            Assert.False(row.HasErrors);
            Assert.Equal(original, row.Snapshot);
            input = BeginCellEdit(window, grid, row, 0);
            input.Text = "25:01:02.003";
            Pump(window);
            Assert.True(panel.CommitEditing());
            Assert.Equal(TimeSpan.FromHours(25).Add(TimeSpan.FromMinutes(1)).Add(TimeSpan.FromMilliseconds(2003)), row.Snapshot.LiveTime);
            Assert.True(original.RealDateTime.EqualsExact(row.Snapshot.RealDateTime));
            grid.Focus();
            Assert.True(panel.TryHandleShortcut("liveMarker.undo", false));
            Assert.Equal(original, Assert.Single(marker.Entries).Snapshot);
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// 切换房间前提交旧表格的有效编辑，旧文档保持未保存，新房间不接收旧数据。
    /// </summary>
    [AvaloniaFact]
    public async Task SwitchingRoomCommitsOldRowBeforeReplacingDataContext()
    {
        var directory = CreateDirectory();
        using var fixture = new LiveRoomFeedViewTestContext();
        fixture.ViewModel.Settings.LiveMarkerAutoSaveEnabled = false;
        var marker = fixture.Room.LiveMarker;
        await OpenDocumentAsync(marker, Path.Combine(directory, "markers.yaml"), WithEntry());
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panel = new LiveMarkerPanelView(context);
        var window = ShowWindow(panel, 900);

        try
        {
            var grid = panel.FindControl<DataGrid>("MarkerDataGrid")!;
            var row = Assert.Single(marker.Entries);
            var input = BeginCellEdit(window, grid, row, 2);
            input.Text = "切换房间前提交的备注";
            Pump(window);
            Assert.Equal("原记录", row.Snapshot.Message);
            var next = fixture.ViewModel.SelectedWorkspace!.AddRoom("200", "第二直播间");
            next.LiveMarker.DraftText = "新房间独立草稿";
            Pump(window);

            Assert.Equal("切换房间前提交的备注", Assert.Single(marker.Entries).Snapshot.Message);
            Assert.True(marker.IsDirty);
            Assert.Same(next, context.CurrentRoom);
            Assert.Same(next, panel.DataContext);
            Assert.Same(next.LiveMarker.Entries, grid.ItemsSource);
            Assert.Empty(next.LiveMarker.Entries);
            Assert.False(next.LiveMarker.HasDocument);
            Assert.False(next.LiveMarker.IsDirty);
            Assert.Equal("新房间独立草稿", next.LiveMarker.DraftText);
            Assert.Empty(marker.DraftText);
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// 有效编辑在卸载时提交，重新附加后仍保留，并可再次编辑及取消。
    /// </summary>
    [AvaloniaFact]
    public async Task DetachingEditedGridPreservesCommitAndAllowsAnotherCancelledEdit()
    {
        var directory = CreateDirectory();
        using var fixture = new LiveRoomFeedViewTestContext();
        fixture.ViewModel.Settings.LiveMarkerAutoSaveEnabled = false;
        var marker = fixture.Room.LiveMarker;
        await OpenDocumentAsync(marker, Path.Combine(directory, "markers.yaml"), WithEntry());
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panel = new LiveMarkerPanelView(context);
        var window = ShowWindow(panel, 900);

        try
        {
            var grid = panel.FindControl<DataGrid>("MarkerDataGrid")!;
            var row = Assert.Single(marker.Entries);
            var input = BeginCellEdit(window, grid, row, 2);
            input.Text = "卸载前提交的备注";
            Pump(window);
            Assert.Equal("原记录", row.Snapshot.Message);

            window.Content = null;
            Pump(window);
            Assert.Equal("卸载前提交的备注", row.Snapshot.Message);
            Assert.True(marker.IsDirty);
            Assert.False(panel.IsClockRunning);
            window.Content = panel;
            Pump(window);
            Assert.Same(row, Assert.Single(marker.Entries));
            Assert.Equal("卸载前提交的备注", row.Message);
            input = BeginCellEdit(window, grid, row, 2);
            input.Text = "这次编辑应取消";
            Pump(window);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Pump(window);

            Assert.Equal("卸载前提交的备注", row.Message);
            Assert.Equal("卸载前提交的备注", row.Snapshot.Message);
            Assert.True(panel.CommitEditing());
            Assert.True(marker.IsDirty);
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// 文件选择后切换房间或释放面板时，迟到结果不能更换任何房间的文档。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LatePickerResultCannotCrossSelectionOrPanelLifetime(bool dispose)
    {
        var directory = CreateDirectory();
        using var fixture = new LiveRoomFeedViewTestContext();
        fixture.ViewModel.Settings.LiveMarkerAutoSaveEnabled = false;
        var marker = fixture.Room.LiveMarker;
        var originalPath = Path.Combine(directory, "first.yaml");
        var nextPath = Path.Combine(directory, "next.yaml");
        await OpenDocumentAsync(marker, originalPath);
        await new LiveMarkerFileService().SaveAsync(new() { StartTime = DateTimeOffset.Now.AddHours(-1) }, nextPath);
        var picker = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panel = new LiveMarkerPanelView(context, (_, _) => Task.FromResult<string?>(null), (_, _) => picker.Task);
        var window = ShowWindow(panel);

        try
        {
            var opening = panel.RunFileActionAsync("liveMarker.open");
            Assert.False(opening.IsCompleted);

            if (dispose)
            {
                panel.Dispose();
            }
            else
            {
                fixture.ViewModel.SelectedWorkspace!.AddRoom("200", "第二直播间");
            }

            picker.SetResult([nextPath]);
            await opening;
            Assert.Equal(originalPath, marker.FilePath);

            if (!dispose)
            {
                Assert.False(context.CurrentRoom!.LiveMarker.HasDocument);
            }
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// 离开确认取消或候选文件无效时，原文档和草稿继续保留。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancelledLeaveOrFailedOpenKeepsCurrentDocument(bool cancel)
    {
        var directory = CreateDirectory();
        using var fixture = new LiveRoomFeedViewTestContext();
        fixture.ViewModel.Settings.LiveMarkerAutoSaveEnabled = false;
        var marker = fixture.Room.LiveMarker;
        var originalPath = Path.Combine(directory, "first.yaml");
        await OpenDocumentAsync(marker, originalPath);
        var nextPath = Path.Combine(directory, "missing.yaml");
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panel = new LiveMarkerPanelView(context, (_, _) => Task.FromResult<string?>(null),
            (_, _) => Task.FromResult<IReadOnlyList<string>>([nextPath]));
        panel.ConfirmLeaveAsync = _ => Task.FromResult(LiveMarkerLeaveDecision.Cancel);
        var window = ShowWindow(panel);

        try
        {
            if (cancel)
            {
                marker.DraftText = "保留草稿";
            }

            await panel.RunFileActionAsync("liveMarker.open");

            Assert.Equal(originalPath, marker.FilePath);
            Assert.True(marker.HasDocument);

            if (cancel)
            {
                Assert.Equal("保留草稿", marker.DraftText);
                Assert.Empty(marker.LastError);
            }
            else
            {
                Assert.NotEmpty(marker.LastError);
            }
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// 面板或祖先隐藏、卸载、释放时停止时钟刷新，重新显示恢复且文档继续存在。
    /// </summary>
    [AvaloniaFact]
    public async Task ClockRefreshFollowsVisibilityAttachmentAndDisposal()
    {
        var directory = CreateDirectory();
        using var fixture = new LiveRoomFeedViewTestContext();
        fixture.ViewModel.Settings.LiveMarkerAutoSaveEnabled = false;
        await OpenDocumentAsync(fixture.Room.LiveMarker, Path.Combine(directory, "markers.yaml"));
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panel = new LiveMarkerPanelView(context);
        var parent = new Border { Child = panel };
        var window = ShowWindow(parent);

        try
        {
            Assert.True(panel.IsClockRunning);
            parent.IsVisible = false;
            Assert.False(panel.IsClockRunning);
            parent.IsVisible = true;
            Assert.True(panel.IsClockRunning);
            window.Content = null;
            Assert.False(panel.IsClockRunning);
            window.Content = parent;
            Pump(window);
            Assert.True(panel.IsClockRunning);
            panel.Dispose();
            Assert.False(panel.IsClockRunning);
            Assert.True(fixture.Room.LiveMarker.HasDocument);
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// 宽窄面板都能显示中文记录和七色示例，并按需输出真实渲染图。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1000)]
    [InlineData(400)]
    public async Task WideAndNarrowPanelsRenderSampleMarkers(int width)
    {
        var directory = CreateDirectory();
        using var fixture = new LiveRoomFeedViewTestContext();
        fixture.ViewModel.Settings.LiveMarkerAutoSaveEnabled = false;
        var marker = fixture.Room.LiveMarker;
        var path = Path.Combine(directory, "中文直播高光记录.yaml");
        await new LiveMarkerFileService().SaveAsync(new()
        {
            StartTime = DateTimeOffset.Now.AddHours(-2),
            Marker = Enum.GetValues<LiveMarkerColor>().Select((color, index) => new LiveMarkerEntry
            {
                LiveTime = TimeSpan.FromMinutes(12 + index * 5).Add(TimeSpan.FromSeconds(3)),
                RealDateTime = new(2026, 10, 20, 20, 12 + index * 5, 3, TimeSpan.FromHours(8)),
                MarkerColor = color,
                Message = index == 1
                    ? "精彩互动：主播分享新歌背后的故事，观众接连回应，这是一条用于检查长中文备注截断和表格滚动的示例记录。"
                    : new[] { "开场问候与今日安排", "", "演唱开始：夜空中的旋律", "突发高光：全场大笑", "观众提问与主播回复", "结束前的感谢", "下次直播预告" }[index]
            }).ToArray()
        }, path);
        await marker.OpenAsync(path);
        marker.ShowRealTimeColumn = width >= 600;
        marker.SelectedColor = LiveMarkerColor.Green;
        marker.DraftText = "记录刚才的精彩片段，按 Enter 添加";
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var panel = new LiveMarkerPanelView(context);
        var window = ShowWindow(panel, width);

        try
        {
            window.Height = 600;
            Pump(window);
            var toolbar = panel.FindControl<WrapPanel>("MarkerFileToolbar")!;
            var input = panel.FindControl<TextBox>("MarkerInput")!;
            var grid = panel.FindControl<DataGrid>("MarkerDataGrid")!;
            Assert.Equal(7, grid.ItemsSource!.Cast<LiveMarkerEntryViewModel>().Count());
            Assert.Equal(7, grid.GetVisualDescendants().OfType<DataGridRow>()
                .Count(row => row.DataContext is LiveMarkerEntryViewModel));
            Assert.True(grid.Bounds.Height > 200);
            Assert.True(input.Bounds.Width >= (width >= 600 ? 600 : 80));
            var inputRow = panel.FindControl<Grid>("MarkerInputRow")!;
            var inputControls = new Control[]
            {
                panel.FindControl<TextBlock>("MarkerInputTime")!,
                panel.FindControl<Button>("MarkerUnlockTimeButton")!,
                panel.FindControl<ComboBox>("MarkerColorSelector")!,
                input,
                panel.FindControl<Button>("MarkerAddButton")!
            };
            Assert.Equal(32, inputRow.Bounds.Height);
            for (var index = 0; index < inputControls.Length; index++)
            {
                var control = inputControls[index];
                Assert.True(control.IsVisible);
                Assert.InRange(control.Bounds.Center.Y, inputRow.Bounds.Height / 2 - 0.5, inputRow.Bounds.Height / 2 + 0.5);
                Assert.InRange(control.Bounds.Left, 0, inputRow.Bounds.Width);
                Assert.InRange(control.Bounds.Right, 0, inputRow.Bounds.Width + 0.5);
                if (index > 0)
                {
                    Assert.True(control.Bounds.Left >= inputControls[index - 1].Bounds.Right);
                }
            }

            foreach (var button in toolbar.Children)
            {
                Assert.InRange(button.Bounds.Right, 0, toolbar.Bounds.Width + 0.5);
                Assert.InRange(button.Bounds.Bottom, 0, toolbar.Bounds.Height + 0.5);
            }

            var error = panel.FindControl<TextBlock>("MarkerLastError")!;
            Assert.False(error.IsVisible);
            var inputBottom = input.TranslatePoint(new(0, input.Bounds.Height), panel)!.Value.Y;
            Assert.InRange(panel.Bounds.Height - inputBottom, 11.5, 12.5);

            var outputDirectory = Environment.GetEnvironmentVariable("DANMUNEO_MARKER_SCREENSHOT_DIRECTORY");

            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                window.MouseMove(new(-1, -1));
                AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
                using var bitmap = window.CaptureRenderedFrame();
                Assert.NotNull(bitmap);
                bitmap.Save(Path.Combine(outputDirectory, $"live-marker-{width}x600.png"));
            }

            marker.ReportError(new IOException("无法保存记录文件，请检查目标文件夹是否仍然可写。"));
            Pump(window);
            Assert.True(error.IsVisible);
            Assert.True(error.Bounds.Height > 0);
            inputBottom = input.TranslatePoint(new(0, input.Bounds.Height), panel)!.Value.Y;
            var errorTop = error.TranslatePoint(default, panel)!.Value.Y;
            var errorBottom = error.TranslatePoint(new(0, error.Bounds.Height), panel)!.Value.Y;
            Assert.InRange(errorTop - inputBottom, 5.5, 6.5);
            Assert.InRange(panel.Bounds.Height - errorBottom, 11.5, 12.5);
            marker.LastError = "";
            Pump(window);
            Assert.False(error.IsVisible);
            inputBottom = input.TranslatePoint(new(0, input.Bounds.Height), panel)!.Value.Y;
            Assert.InRange(panel.Bounds.Height - inputBottom, 11.5, 12.5);
        }
        finally
        {
            window.Close();
            Directory.Delete(directory, true);
        }
    }

    private static TextBox BeginCellEdit(Window window, DataGrid grid, LiveMarkerEntryViewModel row, int column)
    {
        grid.SelectedItem = row;
        grid.CurrentColumn = grid.Columns[column];
        grid.ScrollIntoView(row, grid.Columns[column]);
        grid.Focus();
        Pump(window);
        Assert.True(grid.BeginEdit());
        Pump(window);
        return Assert.Single(grid.GetVisualDescendants().OfType<TextBox>());
    }

    private static Window ShowWindow(Control content, double width = 560)
    {
        var window = new Window { Width = width, Height = 580, Content = content };

        try
        {
            window.Show();
            Pump(window);
            return window;
        }
        catch
        {
            window.Close();
            throw;
        }
    }

    private static async Task OpenDocumentAsync(LiveMarkerViewModel marker, string path, LiveMarkerEntry? entry = null)
    {
        await new LiveMarkerFileService().SaveAsync(new()
        {
            StartTime = DateTimeOffset.Now.AddHours(-1),
            Marker = entry is null ? [] : [entry]
        }, path);
        await marker.OpenAsync(path);
    }

    private static LiveMarkerEntry WithEntry()
    {
        return new()
        {
            RealDateTime = new(2026, 10, 20, 17, 0, 0, TimeSpan.FromHours(8)),
            LiveTime = TimeSpan.FromSeconds(10),
            Message = "原记录",
            MarkerColor = LiveMarkerColor.Blue
        };
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "danmu-marker-panel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
