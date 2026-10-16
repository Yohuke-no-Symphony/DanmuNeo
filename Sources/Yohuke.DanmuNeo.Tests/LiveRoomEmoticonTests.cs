using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 验证监听收到表情后异步刷新图片，以及旧弹幕和停止监听时的更新边界。
/// </summary>
public sealed class LiveRoomEmoticonTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 弹幕先显示原文，图片下载完成后刷新对应项。
    /// </summary>
    [AvaloniaFact]
    public async Task DownloadedEmoticonUpdatesFeedWithoutChangingContent()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new EmoticonHttpHandler { BeforeResponseAsync = (_, token) => release.Task.WaitAsync(token) };
        using var client = new HttpClient(handler);
        using var cache = new EmoticonCacheService(new(directory), client);
        var factory = new FakeLiveDanmuSocketFactory();
        factory.Socket.KeepRunning = true;
        var room = CreateRoom(cache, factory);
        room.StartListening();

        try
        {
            await factory.Socket.StartedTask.WaitAsync(TimeSpan.FromSeconds(3));
            factory.Socket.EmitDanmu(CreateMessage());
            Dispatcher.UIThread.RunJobs();
            var item = Assert.Single(room.DanmuItems);
            Assert.Empty(item.Emoticons);
            Assert.Equal("你好[表情]", item.Content);
            release.SetResult();
            await WaitForAsync(() => item.Emoticons.Count == 1);

            Assert.True(File.Exists(Assert.Single(item.Emoticons).ImagePath));
            Assert.Equal("你好[表情]", item.Content);
        }
        finally
        {
            release.TrySetResult();
            room.StopListening();
            await factory.Socket.DisposedTask.WaitAsync(TimeSpan.FromSeconds(3));
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>
    /// 停止监听或移除旧弹幕后，迟到的下载结果不会更新已失效的弹幕项。
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateImageDoesNotUpdateInactiveFeedItem(bool stopListening)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new EmoticonHttpHandler { BeforeResponseAsync = (_, token) => release.Task.WaitAsync(token) };
        using var client = new HttpClient(handler);
        using var cache = new EmoticonCacheService(new(directory), client);
        var factory = new FakeLiveDanmuSocketFactory();
        factory.Socket.KeepRunning = true;
        var room = CreateRoom(cache, factory);
        room.StartListening();

        try
        {
            await factory.Socket.StartedTask.WaitAsync(TimeSpan.FromSeconds(3));
            factory.Socket.EmitDanmu(CreateMessage());
            Dispatcher.UIThread.RunJobs();
            var item = Assert.Single(room.DanmuItems);

            if (stopListening)
            {
                room.StopListening();
            }
            else
            {
                room.DanmuItems.Clear();
            }

            release.SetResult();
            await cache.GetEmoticonAsync("https://i0.hdslb.com/room.png");
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(item.Emoticons);
        }
        finally
        {
            release.TrySetResult();
            room.StopListening();
            await factory.Socket.DisposedTask.WaitAsync(TimeSpan.FromSeconds(3));
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }

        GC.SuppressFinalize(this);
    }

    private static LiveRoomTabViewModel CreateRoom(IEmoticonCacheService cache, ILiveDanmuSocketFactory factory)
    {
        var settings = new AppSettings();
        var account = new BilibiliAccount { Cookie = "SESSDATA=test" };
        var room = new LiveRoomTabViewModel(new() { RoomId = "100" });
        room.Configure(_ => account, (_, _) => account, () => settings,
            () => MarkSymbolService.CreateDefaultGroup(settings), () => [], _ => [],
            new FakeDanmuSendService(), new FakeRoomAvatarService(), () => Task.CompletedTask,
            liveDanmuSocketFactory: factory, emoticonCacheService: cache);
        return room;
    }

    private static BilibiliDanmuMessage CreateMessage()
    {
        return new()
        {
            RoomId = "100", Uid = 42, UserName = "表情用户", Content = "你好[表情]",
            Emoticons = [new() { Text = "[表情]", Url = "https://i0.hdslb.com/room.png" }]
        };
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 200 && !predicate(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Dispatcher.UIThread.RunJobs();
        Assert.True(predicate());
    }
}
