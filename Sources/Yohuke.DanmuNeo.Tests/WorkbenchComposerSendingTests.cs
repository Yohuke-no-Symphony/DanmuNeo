using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Yohuke.DanmuNeo.Layouts;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.Views.Components;
using Yohuke.DanmuNeo.Windowing;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>通过真实工作台按钮验证长中文草稿在布局迁移后的完整发送。</summary>
public class WorkbenchComposerSendingTests
{
    /// <summary>长句在主窗和浮窗内均可点击发送，并按账号限制保留全部正文和每段符号。</summary>
    [AvaloniaTheory]
    [InlineData(WorkspaceLayoutPresets.DEFAULT, false, 20)]
    [InlineData(WorkspaceLayoutPresets.TRANSLATION, false, 30)]
    [InlineData(WorkspaceLayoutPresets.DEFAULT, true, 20)]
    public async Task ClickingSendAfterLayoutChangeDeliversAllLongDraftSegments(string preset, bool floating, int limit)
    {
        using var context = new MarkGroupCycleTestContext();
        using var handler = new DanmuSendHttpHandler { ResponseJson = "{\"code\":0,\"data\":{}}" };
        using var client = new HttpClient(handler);
        var config = new FakeBilibiliDanmuConfigService { Config = new() { Length = limit } };
        var sender = new DanmuSendService(client, config);
        var account = new BilibiliAccount { Id = "offline", Cookie = "SESSDATA=test;bili_jct=test;DedeUserID=42" };
        var room = context.ViewModel.SelectedWorkspace!.SelectedRoom!;
        var settings = context.ViewModel.Settings;
        settings.SendIntervalMs = 0;
        var marks = new MarkSymbolGroup { TranslateOpenMark = "【", TranslateCloseMark = "】" };
        room.Configure(_ => account, (_, _) => account, () => settings, () => marks, () => [marks], _ => [],
            sender, new FakeRoomAvatarService(), () => Task.CompletedTask, danmuConfigService: config);
        using var workbench = new WorkbenchContext(context.ViewModel);
        using var composer = new DraftComposerPanelView(workbench);
        var main = new Window { Width = 1280, Height = 820 };
        var views = WorkbenchPanelIds.All.ToDictionary(id => id, _ => (Control)new TextBox());
        views[WorkbenchPanelIds.COMPOSER] = composer;
        var store = new WorkspaceLayoutStore(Path.GetDirectoryName(context.StateService.SettingsFilePath)!);
        await using var layouts = new WorkbenchLayoutController(main, views, WorkspaceLayoutPresets.CreateDefaultFile(), store);
        main.Content = layouts.Host;

        try
        {
            main.Show();
            layouts.ApplyPreset(preset);

            if (floating)
            {
                layouts.FloatPanel(WorkbenchPanelIds.COMPOSER);
            }

            Dispatcher.UIThread.RunJobs();
            main.UpdateLayout();
            var owner = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(composer));
            var body = string.Concat(Enumerable.Repeat("这是布局调整后需要完整发送的中文长句，", 8));
            room.InputDraft = body;
            Dispatcher.UIThread.RunJobs();
            owner.UpdateLayout();
            var button = composer.GetVisualDescendants().OfType<Button>()
                .Single(control => control.Classes.Contains("translateComposerSendButton"));
            Assert.True(button.IsEffectivelyEnabled);
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), owner)!.Value;
            Assert.InRange(point.X, 0, owner.ClientSize.Width);
            Assert.InRange(point.Y, 0, owner.ClientSize.Height);
            owner.MouseDown(point, MouseButton.Left);
            owner.MouseUp(point, MouseButton.Left);
            Assert.NotNull(room.SendDraftCommand.ExecutionTask);
            await room.SendDraftCommand.ExecutionTask.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();

            Assert.False(room.IsDraftSendFailed, room.DraftSendError);
            Assert.Equal("", room.InputDraft);
            Assert.True(handler.Forms.Count > 1);
            var messages = handler.Forms.Select(form => form["msg"]).ToArray();
            Assert.All(messages, message =>
            {
                Assert.InRange(message.Length, 3, limit);
                Assert.StartsWith("【", message);
                Assert.EndsWith("】", message);
            });
            Assert.Equal(body, string.Concat(messages.Select((message, index) => message[(index == 0 ? 1 : 2)..^1])));
        }
        finally
        {
            await layouts.DisposeAsync();
            main.Close();
        }
    }
}
