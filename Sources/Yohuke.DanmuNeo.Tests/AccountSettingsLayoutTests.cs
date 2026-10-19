using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.ViewModels.Items;
using Yohuke.DanmuNeo.Views.Components;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>验证账号设置的紧凑布局、凭据隐藏、状态灯与唯一默认开关。</summary>
[Collection(AvaloniaDispatcherTestCollection.NAME)]
public class AccountSettingsLayoutTests
{
    /// <summary>不同宽度及主题下不显示凭据，状态灯与单行名称并排且默认按钮可以切换。</summary>
    [AvaloniaTheory]
    [InlineData(320, false, "zh-CN")]
    [InlineData(320, true, "en-US")]
    [InlineData(520, false, "zh-CN")]
    [InlineData(520, true, "zh-CN")]
    [InlineData(720, false, "en-US")]
    [InlineData(720, true, "zh-CN")]
    public async Task CompactRowsHideCredentialsAndShowLiveValidation(int width, bool dark, string language)
    {
        using var localization = new LocalizationTestScope();
        Localization.SetLanguage(language);
        await using var fixture = new AccountLoginTestContext(
            new() { Name = "主要账号", Cookie = "SESSDATA=offline-one;bili_jct=offline", IsGlobalDefault = true },
            new() { Name = "我的第二个账号", Cookie = "SESSDATA=offline-two;bili_jct=offline" },
            new() { Name = "名称很长的账号用于验证输入框的宽度限制", Cookie = "SESSDATA=offline-three;bili_jct=offline" });
        var application = fixture.Application;
        application.SelectedSettingsSection = application.SettingsSectionOptions.Single(option => option.Key == "account");
        var validations = application.AccountItems.Select(item => item.RefreshAsync()).ToArray();
        fixture.Validation.Requests[0].SetResult(new()
        {
            Status = AccountValidationStatus.Valid, Uid = 42, UserName = "登录用户名", CheckedAt = DateTimeOffset.Now
        });
        fixture.Validation.Requests[1].SetResult(new() { Status = AccountValidationStatus.Expired, CheckedAt = DateTimeOffset.Now });
        fixture.Validation.Requests[2].SetResult(new() { Status = AccountValidationStatus.Unavailable, CheckedAt = DateTimeOffset.Now });
        await Task.WhenAll(validations);
        var view = new AccountSettingsView { DataContext = application };
        var window = new Window
        {
            Width = width, Height = 540, Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var editors = view.GetVisualDescendants().OfType<TextBox>().ToArray();
            Assert.Equal(3, editors.Length);
            var nameWidth = editors[0].Bounds.Width;
            if (width >= 520)
            {
                Assert.InRange(nameWidth, 219.5, 220.5);
            }
            Assert.All(editors, editor =>
            {
                Assert.Equal("AccountNameEditor", editor.Name);
                Assert.InRange(editor.Bounds.Width, 1, 220);
                Assert.InRange(editor.Bounds.Width, nameWidth - 0.5, nameWidth + 0.5);
                Assert.InRange(editor.Bounds.Height, 31.5, 32.5);
                Assert.DoesNotContain("SESSDATA", editor.Text);
                var indicator = Assert.IsType<Grid>(editor.Parent).GetVisualChildren()
                    .OfType<Avalonia.Controls.Shapes.Ellipse>().Single();
                var editorCenter = editor.TranslatePoint(new(0, editor.Bounds.Height / 2), view)!.Value.Y;
                var lightCenter = indicator.TranslatePoint(new(0, indicator.Bounds.Height / 2), view)!.Value.Y;
                Assert.InRange(lightCenter, editorCenter - 1, editorCenter + 1);
                var origin = editor.TranslatePoint(default, view)!.Value;
                Assert.InRange(origin.X + editor.Bounds.Width, 0, view.Bounds.Width);
                Assert.NotNull(ToolTip.GetTip(indicator));
                var row = Assert.IsType<AccountSettingsItemViewModel>(editor.DataContext);
                var expected = row.IsValid ? "#22C55E" : row.IsInvalid ? "#EF4444" : "#F59E0B";
                Assert.Equal(Color.Parse(expected), Assert.IsAssignableFrom<ISolidColorBrush>(indicator.Fill).Color);
            });
            var toggles = view.GetVisualDescendants().OfType<RadioButton>().ToArray();
            Assert.Equal(3, toggles.Length);
            Assert.True(toggles[0].IsChecked);
            Assert.False(toggles[1].IsChecked);
            var selectedFill = Assert.IsAssignableFrom<ISolidColorBrush>(toggles[0].GetVisualDescendants()
                .OfType<ContentPresenter>().Single().Background);
            Assert.InRange(selectedFill.Opacity * selectedFill.Color.A / 255d, 0.01, 0.2);
            foreach (var control in view.GetVisualDescendants().OfType<Button>().Where(button => button.Name?.StartsWith("Account") == true))
            {
                var origin = control.TranslatePoint(default, view)!.Value;
                Assert.InRange(origin.X + control.Bounds.Width, 0, view.Bounds.Width);
            }
            SavePreview(window, $"accounts-{width}-{(dark ? "dark" : "light")}-{language}");
            Click(window, toggles[1]);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(toggles[1].Command).ExecutionTask!;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(application.Accounts[1], Assert.Single(application.Accounts, account => account.IsGlobalDefault));
            Assert.True(toggles[1].IsChecked);
            Assert.False(toggles[0].IsChecked);
            Click(window, toggles[1]);
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(toggles[1].Command).ExecutionTask!;
            Assert.True(toggles[1].IsChecked);
            Assert.Single(application.Accounts, account => account.IsGlobalDefault);
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
        var directory = Environment.GetEnvironmentVariable("YOHUKE_ACCOUNT_SETTINGS_QA_OUTPUT");
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
