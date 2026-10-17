using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.BrowserLogin;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证已打开界面的静态文本、动态菜单和窗口状态随语言切换刷新。
/// </summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public sealed class ViewsLocalizationTests
{
    /// <summary>
    /// 歌词提示和无障碍名称即时刷新，歌词实例、选择和播放位置保持不变。
    /// </summary>
    [AvaloniaFact]
    public void OpenLyricsPanelRefreshesTextWithoutChangingLyricsOrPlaybackState()
    {
        using var language = new LocalizationTestScope();
        using var context = new LyricNavigationViewTestContext();
        Localization.SetLanguage("zh-CN");
        using var view = new LyricsPanelView(new(context.ViewModel, context.Room));
        var window = new Window { Width = 700, Height = 700, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var line = context.Room.Lyrics[1];
            context.Room.SeekLyricLineStart(line);
            var lines = context.Room.Lyrics.ToArray();
            var selector = view.FindControl<ComboBox>("LyricSendModeSelector")!;
            var selectedMode = selector.SelectedValue;
            var button = view.FindControl<Button>("LyricStopButton")!;
            var command = button.Command;
            var chineseTip = ToolTip.GetTip(button);

            Localization.SetLanguage("en-US");
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(chineseTip, ToolTip.GetTip(button));
            Assert.Equal(Localization.Get("UI.LyricsPanelView.StopAndReset"), ToolTip.GetTip(button));
            Assert.Equal(Localization.Get("UI.LyricsPanelView.StopAndReset"), AutomationProperties.GetName(button));
            Assert.Same(command, button.Command);
            Assert.Same(selector, view.FindControl<ComboBox>("LyricSendModeSelector"));
            Assert.Equal(selectedMode, selector.SelectedValue);
            Assert.Equal(lines, context.Room.Lyrics.ToArray());
            Assert.All(lines, original => Assert.Contains(context.Room.Lyrics, current => ReferenceEquals(original, current)));
            Assert.Equal(10, context.Room.LyricPlaybackPositionSeconds, 3);
            Assert.False(context.Room.IsLyricAutoSending);
            Assert.Empty(context.SendService.SentMessages);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 动态菜单只在打开期间订阅语言变化，重新打开时读取当前语言且保留命令。
    /// </summary>
    [AvaloniaFact]
    public void DynamicMenuRefreshesWhileOpenAndReleasesItsLanguageSubscriptionWhenClosed()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        var invocations = 0;
        var command = new RelayCommand(() => invocations++);
        var item = new MenuItem { Command = command };
        var menu = new LocalizedContextMenu { ItemsSource = new[] { item } };
        menu.SetHeader(item, "UI.WorkbenchFeedInteraction.CopyText");
        var target = new Button { Width = 120, Height = 40 };
        var window = new Window { Width = 400, Height = 300, Content = target };

        try
        {
            window.Show();
            menu.Open(target);
            Dispatcher.UIThread.RunJobs();
            Assert.True(menu.IsOpen);
            var chineseHeader = item.Header;

            Localization.SetLanguage("en-US");

            Assert.NotEqual(chineseHeader, item.Header);
            Assert.Equal(Localization.Get("UI.WorkbenchFeedInteraction.CopyText"), item.Header);
            Assert.Same(command, item.Command);
            Assert.Equal(0, invocations);
            menu.Close();
            var closedHeader = item.Header;

            Localization.SetLanguage("zh-CN");

            Assert.Equal(closedHeader, item.Header);
            menu.Open(target);
            Assert.Equal(Localization.Get("UI.WorkbenchFeedInteraction.CopyText"), item.Header);
            item.Command!.Execute(null);
            Assert.Equal(1, invocations);
        }
        finally
        {
            menu.Close();
            window.Close();
        }
    }

    /// <summary>
    /// 切换语言更新已置顶窗口的提示和无障碍名称，保留置顶状态。
    /// </summary>
    [AvaloniaFact]
    public void OpenTitleBarRefreshesPinnedTooltipWithoutChangingTopmostState()
    {
        using var language = new LocalizationTestScope();
        using var context = new LocalizationSettingsTestContext("zh-CN");
        var titleBar = new TitleBarView { DataContext = context.ViewModel };
        var window = new Window { Width = 1000, Height = 400, Content = titleBar };

        try
        {
            window.Show();
            var pin = titleBar.FindControl<Button>("PinButton")!;
            pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(window.Topmost);
            var chineseTip = ToolTip.GetTip(pin);

            Localization.SetLanguage("en-US");
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.Topmost);
            Assert.NotEqual(chineseTip, ToolTip.GetTip(pin));
            Assert.Equal(Localization.Get("UI.TitleBarView.UnpinWindow"), ToolTip.GetTip(pin));
            Assert.Equal(Localization.Get("UI.TitleBarView.UnpinWindow"), AutomationProperties.GetName(pin));
            Assert.Contains("selected", pin.Classes);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 更新弹窗中的格式化版本提示即时刷新且保留当前版本数据。
    /// </summary>
    [AvaloniaFact]
    public void OpenUpdateDialogRefreshesVersionFormatWithoutChangingVersion()
    {
        using var language = new LocalizationTestScope();
        using var context = new LocalizationSettingsTestContext("zh-CN");
        var view = new AppUpdateDialogView { DataContext = context.ViewModel };
        var window = new Window { Width = 700, Height = 500, Content = view };

        try
        {
            window.Show();
            var version = context.ViewModel.AppVersion;
            var label = view.FindControl<TextBlock>("CurrentVersionText")!;
            var chineseText = label.Text;

            Localization.SetLanguage("en-US");
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(chineseText, label.Text);
            Assert.Equal(Localization.Format("UI.AppUpdateDialogView.CurrentVersion", version), label.Text);
            Assert.Equal(version, context.ViewModel.AppVersion);
            Assert.Same(context.ViewModel, view.DataContext);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 浏览器离线初始化错误在切换语言后仍保留原始诊断且不重试创建原生浏览器。
    /// </summary>
    [AvaloniaFact]
    public async Task FailedBrowserShellRefreshesLanguageWithoutRecreatingTheBrowser()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        var creations = 0;
        var window = new BrowserCookieLoginWindow(BrowserLoginPlatform.Bilibili, () =>
        {
            creations++;
            throw new NotSupportedException("offline-marker");
        });

        try
        {
            window.Show();
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            var placeholder = window.FindControl<TextBlock>("WebViewPlaceholderText")!;
            var status = window.FindControl<TextBlock>("StatusText")!;
            var chinesePlaceholder = placeholder.Text;

            Localization.SetLanguage("en-US");

            Assert.Equal(1, creations);
            Assert.NotEqual(chinesePlaceholder, placeholder.Text);
            Assert.Equal(Localization.Get("UI.BrowserCookieLoginWindow.InitializationFailed"), placeholder.Text);
            Assert.Contains("offline-marker", status.Text);
            Assert.Same(placeholder, window.FindControl<Border>("WebViewHost")!.Child);
            Assert.True(placeholder.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// 保存时触发的空标题校验在已打开编辑窗口内完整切换语言，保留草稿和原条目。
    /// </summary>
    [AvaloniaFact]
    public void OpenLyricEditorRefreshesTheCauseOfAnExistingEmptyTitleError()
    {
        using var language = new LocalizationTestScope();
        Localization.SetLanguage("zh-CN");
        var item = new LyricLibraryItem
        {
            Title = "原始曲名", Artist = "原始歌手", LyricText = "[00:01]原始歌词", Tags = "原始标签"
        };
        var window = new LyricEditDialogWindow(item);

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var title = window.FindControl<TextBox>("LyricTitleInput")!;
            title.Text = "";
            Dispatcher.UIThread.RunJobs();
            window.FindControl<Button>("SaveLyricButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var model = Assert.IsType<LyricEditDialogViewModel>(window.DataContext);
            var errorText = window.FindControl<TextBlock>("LyricValidationErrorText")!;
            Assert.True(model.HasError);
            Assert.Contains("歌曲名不能为空。", errorText.Text);
            var chineseError = errorText.Text;

            Localization.SetLanguage("en-US");
            Dispatcher.UIThread.RunJobs();

            Assert.NotEqual(chineseError, errorText.Text);
            Assert.Equal(Localization.Format("UI.LyricEditDialogWindow.ValidationFailed",
                Localization.Get("Services.LyricLibrary.TitleRequired")), errorText.Text);
            Assert.Equal(model.ErrorText, errorText.Text);
            Assert.True(window.IsVisible);
            Assert.Equal("", title.Text);
            Assert.Equal("[00:01]原始歌词", window.FindControl<TextBox>("OriginalLyricTextBox")!.Text);
            Assert.Equal("原始曲名", item.Title);
            Assert.Equal("原始歌手", item.Artist);
            Assert.Equal("原始标签", item.Tags);
        }
        finally
        {
            window.Close();
        }
    }
}
