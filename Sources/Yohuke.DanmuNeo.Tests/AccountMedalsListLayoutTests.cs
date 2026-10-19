using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证粉丝牌列表在不同文字长度、状态、主题与宽度下的实际排版。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public class AccountMedalsListLayoutTests
{
    /// <summary>两行内容保持共同左边缘与等级右边缘，佩戴状态以徽章显示且列表融入背景。</summary>
    [AvaloniaTheory]
    [InlineData(240, false, "zh-CN")]
    [InlineData(240, true, "en-US")]
    [InlineData(320, false, "zh-CN")]
    [InlineData(320, true, "zh-CN")]
    [InlineData(640, false, "zh-CN")]
    [InlineData(640, true, "zh-CN")]
    [InlineData(320, false, "en-US")]
    [InlineData(640, true, "en-US")]
    public async Task MedalRowsKeepAlignmentAndUseInlineWearingBadge(int width, bool dark, string language)
    {
        using var localization = new LocalizationTestScope();
        Localization.SetLanguage(language);
        using var fixture = new LiveRoomFeedViewTestContext();
        using var context = new WorkbenchContext(fixture.ViewModel);
        using var log = new AppLogService();
        using var sessions = new BilibiliAccountSessionService(new MedalValidationService());
        var account = MedalSwitchCoordinatorTests.Account();
        account.Name = "离线测试账号";
        sessions.Register(account);
        await sessions.ValidateAsync(account);
        var medals = new AccountMedal[]
        {
            new(1, 623441612, "牵丝蝠", 35, true, "椎名莱羽Official"),
            new(2, 2299184, "打工鬼", 24, false, "古守血遊official"),
            new(3, 3493105972021270, "超过显示宽度的粉丝牌名称", 122, false, "星熊ruru与非常长的主播名称"),
            new(4, 6226974, "kkq", 6, false, "圈_circle")
        };
        var service = new FakeMedalService { Collection = new(medals, true) };
        await using var coordinator = new MedalSwitchCoordinator(service, sessions, () => Task.CompletedTask,
            () => TimeSpan.FromSeconds(3), log);
        using var viewModel = new AccountMedalsViewModel(new([account]), coordinator, log);
        viewModel.UpdateCurrentAccount(account);
        using var panel = new AccountMedalsPanelView(context, viewModel);
        var window = new Window
        {
            Width = width, Height = 720, Content = panel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            window.Show();
            await viewModel.ActivateAsync();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var list = panel.FindControl<ListBox>("MedalList")!;
            Assert.Equal(medals.Length, list.ItemCount);
            Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(list.Background).Color.A);
            Assert.DoesNotContain(list.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text?.StartsWith("UID ", StringComparison.Ordinal) == true);
            double? left = null;
            double? right = null;
            double? rowHeight = null;
            foreach (var medal in medals)
            {
                var row = list.GetRealizedContainers().OfType<ListBoxItem>()
                    .Single(item => ReferenceEquals(item.DataContext, medal));
                var texts = row.GetVisualDescendants().OfType<TextBlock>().ToArray();
                var name = texts.Single(text => text.Text == medal.Name);
                var owner = texts.Single(text => text.Text == medal.TargetName);
                var level = texts.Single(text => text.Text == $"Lv.{medal.Level}");
                var nameLeft = name.TranslatePoint(default, list)!.Value.X;
                var ownerLeft = owner.TranslatePoint(default, list)!.Value.X;
                var levelRight = level.TranslatePoint(new(level.Bounds.Width, 0), list)!.Value.X;
                left ??= nameLeft;
                right ??= levelRight;
                rowHeight ??= row.Bounds.Height;
                Assert.InRange(nameLeft, left.Value - 0.5, left.Value + 0.5);
                Assert.InRange(ownerLeft, left.Value - 0.5, left.Value + 0.5);
                Assert.InRange(levelRight, right.Value - 0.5, right.Value + 0.5);
                Assert.InRange(row.Bounds.Height, rowHeight.Value - 0.5, rowHeight.Value + 0.5);
                Assert.InRange(levelRight, 0, list.Bounds.Width);
                var badge = Assert.Single(row.GetVisualDescendants().OfType<Border>(),
                    border => border.Classes.Contains("medalWearingBadge"));
                Assert.Equal(medal.IsWearing, badge.IsEffectivelyVisible);
                if (medal.IsWearing)
                {
                    Assert.InRange(badge.TranslatePoint(new(badge.Bounds.Width, 0), list)!.Value.X,
                        levelRight - 0.5, levelRight + 0.5);
                }
                Assert.Equal(medal.Name, ToolTip.GetTip(name));
                Assert.Equal(medal.TargetName, ToolTip.GetTip(owner));
            }

            SavePreview(window, $"medals-{width}-{(dark ? "dark" : "light")}-{language}");
            viewModel.SelectedMedal = medals[1];
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            foreach (var row in list.GetRealizedContainers().OfType<ListBoxItem>())
            {
                var medal = Assert.IsType<AccountMedal>(row.DataContext);
                var texts = row.GetVisualDescendants().OfType<TextBlock>().ToArray();
                var name = texts.Single(text => text.Text == medal.Name);
                var level = texts.Single(text => text.Text == $"Lv.{medal.Level}");
                Assert.InRange(name.TranslatePoint(default, list)!.Value.X, left!.Value - 0.5, left.Value + 0.5);
                Assert.InRange(level.TranslatePoint(new(level.Bounds.Width, 0), list)!.Value.X,
                    right!.Value - 0.5, right.Value + 0.5);
            }
            Assert.Same(medals[1], list.SelectedItem);
            var selected = list.GetRealizedContainers().OfType<ListBoxItem>().Single(row => row.IsSelected);
            var selectedBackground = Assert.IsAssignableFrom<ISolidColorBrush>(selected
                .GetVisualDescendants().OfType<ContentPresenter>().Single().Background);
            Assert.InRange(selectedBackground.Opacity * selectedBackground.Color.A / 255d, 0.01, 0.2);
            SavePreview(window, $"medals-{width}-{(dark ? "dark" : "light")}-{language}-selected");
        }
        finally
        {
            window.Close();
        }
    }

    private static void SavePreview(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("YOHUKE_MEDALS_QA_OUTPUT");
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
