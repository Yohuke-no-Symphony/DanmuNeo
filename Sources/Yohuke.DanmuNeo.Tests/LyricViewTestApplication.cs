using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Dock.Avalonia.Themes.Fluent;

[assembly: AvaloniaTestApplication(typeof(Yohuke.DanmuNeo.Tests.LyricViewTestApplication))]

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 使用正式主题和绘图后端的歌词界面测试应用。
/// </summary>
public class LyricViewTestApplication : Application
{
    /// <summary>
    /// 创建无原生窗口的测试应用。
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<LyricViewTestApplication>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            });
    }

    /// <inheritdoc/>
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new DockFluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Yohuke.DanmuNeo/"))
        {
            Source = new("avares://Yohuke.DanmuNeo/Views/Styles/WorkbenchTheme.axaml")
        });
    }
}
