using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 使用独立配置目录和离线歌词数据的界面测试上下文。
/// </summary>
public sealed class LyricViewTestContext : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 初始化歌词界面测试数据。
    /// </summary>
    public LyricViewTestContext(int resultCount = 24)
    {
        var state = new AppState
        {
            Workspaces = [new() { Name = "歌词布局测试" }]
        };
        var titles = new[]
        {
            "很长的中文歌曲名称，用于确认歌名不会挤压操作按钮",
            "月光・日本語の長い曲名と歌手名を表示するテスト",
            "An Extended English Song Title That Must Leave Room For Actions"
        };

        for (var index = 0; index < resultCount; index++)
        {
            state.LyricLibrary.Add(new()
            {
                Title = titles[index % titles.Length],
                Artist = $"歌手 {index}",
                Album = "很长的专辑名称・Extended Album Name",
                Source = index % 2 == 0 ? "wy" : "qq",
                SourceSongId = index.ToString(),
                LyricText = "[00:01]测试歌词"
            });
        }

        var service = new AppStateService(directory);
        service.Save(state);
        ViewModel = new(service, danmuConfigService: new FakeBilibiliDanmuConfigService());

        foreach (var item in state.LyricLibrary)
        {
            ViewModel.MusicLyricSearchResults.Add(new()
            {
                Title = item.Title,
                Artist = item.Artist,
                Album = item.Album,
                Source = item.Source,
                SourceSongId = item.SourceSongId
            });
        }
    }

    /// <summary>
    /// 供控件绑定的正式视图模型。
    /// </summary>
    public MainWindowViewModel ViewModel { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        ViewModel.Dispose();
        Directory.Delete(directory, true);
    }
}
