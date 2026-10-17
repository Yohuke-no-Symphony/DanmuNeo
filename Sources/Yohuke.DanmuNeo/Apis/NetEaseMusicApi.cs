using Yohuke.DanmuNeo.Apis.Models.Music;

namespace Yohuke.DanmuNeo.Apis;

/// <summary>
/// 网易云音乐搜索、歌词和歌曲详情 API。
/// </summary>
public class NetEaseMusicApi : BaseApi
{
    private static readonly string[] CN_IP =
    [
        "110.42",
        "222.206",
        "220.180",
        "180.163",
        "113.100",
        "125.83",
        "183.140",
        "49.78",
        "106.230",
        "223.150"
    ];

    /// <summary>
    /// 初始化网易云音乐 API；注入的 HTTP 客户端由调用方管理。
    /// </summary>
    public NetEaseMusicApi(TimeSpan? timeout = null, HttpClient? httpClient = null)
        : base(httpClient, timeout)
    {
    }

    /// <summary>
    /// 按关键字搜索歌曲。
    /// </summary>
    public Task<NetEaseSearchResponse> SearchSongsAsync(
        string keyword,
        int limit = 10,
        bool changeIp = false,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<NetEaseSearchResponse>(
            "https://music.163.com/api/search/get/web",
            new Dictionary<string, object?>
            {
                ["s"] = keyword,
                ["limit"] = limit,
                ["type"] = 1
            },
            CreateHeaders(changeIp),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 根据歌曲 ID 获取歌词。
    /// </summary>
    public Task<NetEaseLyricResponse> GetLyricAsync(
        long songId,
        bool changeIp = false,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<NetEaseLyricResponse>(
            "https://music.163.com/api/song/lyric",
            new Dictionary<string, object?>
            {
                ["id"] = songId,
                ["lv"] = -1,
                ["tv"] = -1
            },
            CreateHeaders(changeIp),
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// 根据歌曲 ID 获取歌曲信息。
    /// </summary>
    public Task<NetEaseSongDetailResponse> GetSongInfoAsync(
        long songId,
        bool changeIp = false,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        return GetJsonAsync<NetEaseSongDetailResponse>(
            "https://music.163.com/api/song/detail",
            new Dictionary<string, object?>
            {
                ["id"] = songId,
                ["ids"] = $"[{songId}]"
            },
            CreateHeaders(changeIp),
            timeout,
            cancellationToken);
    }

    private static Dictionary<string, string> CreateHeaders(bool changeIp)
    {
        var headers = CreateDefaultHeaders();

        if (changeIp)
        {
            var prefix = CN_IP[Random.Shared.Next(0, CN_IP.Length)];
            headers["X-Real-IP"] = $"{prefix}.{Random.Shared.Next(10, 251)}.{Random.Shared.Next(10, 251)}";
        }

        return headers;
    }
}
