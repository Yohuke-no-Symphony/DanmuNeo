using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证禁言面板实际输入框对齐、账号图标开关及窄宽排版。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public class ModerationPanelLayoutTests
{
    /// <summary>账号、目标房间、搜索栏共用边缘，跟随开关可直接点击且始终方形。</summary>
    [AvaloniaTheory]
    [InlineData(240, false, "zh-CN")]
    [InlineData(240, true, "en-US")]
    [InlineData(320, false, "zh-CN")]
    [InlineData(320, true, "zh-CN")]
    [InlineData(640, false, "zh-CN")]
    [InlineData(640, true, "en-US")]
    public async Task InputsAlignAndFollowToggleKeepsPinnedAccount(int width, bool dark, string language)
    {
        using var localization = new LocalizationTestScope();
        Localization.SetLanguage(language);
        using var main = new LiveRoomFeedViewTestContext();
        using var fixture = new ModerationManagerTestContext();
        using var context = new WorkbenchContext(main.ViewModel);
        fixture.Accounts[0].Name = "名称很长的离线测试管理账号";
        using var panel = new ModerationPanelView(context, fixture.Manager);
        var window = new Window
        {
            Width = width, Height = 720, Content = panel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            window.Show();
            await fixture.Manager.RefreshTask;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var account = panel.FindControl<ComboBox>("ModerationAccount")!;
            var follow = panel.FindControl<ToggleButton>("ModerationFollowAccount")!;
            var refresh = panel.FindControl<Button>("ModerationRefresh")!;
            var room = panel.FindControl<ComboBox>("ModerationTargetRoom")!;
            var search = panel.FindControl<TextBox>("ModerationSearch")!;
            var find = panel.FindControl<Button>("ModerationFindUser")!;
            var left = account.TranslatePoint(default, panel)!.Value.X;
            var right = refresh.TranslatePoint(new(refresh.Bounds.Width, 0), panel)!.Value.X;
            Assert.InRange(room.TranslatePoint(default, panel)!.Value.X, left - 0.5, left + 0.5);
            Assert.InRange(search.TranslatePoint(default, panel)!.Value.X, left - 0.5, left + 0.5);
            Assert.InRange(room.TranslatePoint(new(room.Bounds.Width, 0), panel)!.Value.X, right - 0.5, right + 0.5);
            Assert.InRange(find.TranslatePoint(new(find.Bounds.Width, 0), panel)!.Value.X, right - 0.5, right + 0.5);
            Assert.InRange(right, 0, panel.Bounds.Width);
            Assert.Equal(account.Bounds.Y, follow.Bounds.Y);
            Assert.Equal(follow.Bounds.Y, refresh.Bounds.Y);
            Assert.Equal(search.Bounds.Y, find.Bounds.Y);
            Assert.InRange(follow.Bounds.Width, follow.Bounds.Height - 0.5, follow.Bounds.Height + 0.5);
            Assert.InRange(account.Bounds.Height, follow.Bounds.Height - 0.5, follow.Bounds.Height + 0.5);
            Assert.InRange(search.Bounds.Height, find.Bounds.Height - 0.5, find.Bounds.Height + 0.5);
            Assert.True(account.Bounds.Right < follow.Bounds.Left);
            Assert.True(follow.Bounds.Right < refresh.Bounds.Left);
            Assert.True(search.Bounds.Right < find.Bounds.Left);
            Assert.True(follow.IsChecked);
            Assert.False(account.IsEnabled);
            SavePreview(window, $"moderation-aligned-{width}-{(dark ? "dark" : "light")}-{language}");

            Click(window, follow);
            Assert.False(fixture.Manager.FollowCurrentAccount);
            Assert.True(account.IsEnabled);
            fixture.Manager.UpdateCurrentAccount(fixture.Accounts[1]);
            Assert.Same(fixture.Accounts[0], fixture.Manager.SelectedAccount);
            Click(window, follow);
            await fixture.Manager.RefreshTask;
            Assert.True(fixture.Manager.FollowCurrentAccount);
            Assert.Same(fixture.Accounts[1], account.SelectedItem);
            Assert.False(account.IsEnabled);
            var searchWidth = search.Bounds.Width;
            fixture.Manager.HasMoreUsers = true;
            window.UpdateLayout();
            Assert.InRange(search.Bounds.Width, searchWidth - 0.5, searchWidth + 0.5);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Click(Window window, Control control)
    {
        var center = control.TranslatePoint(new(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseMove(center);
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void SavePreview(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("YOHUKE_TOOLS_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        Directory.CreateDirectory(directory);
        window.MouseMove(new(-1, -1));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(2);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name + ".png"));
    }
}
