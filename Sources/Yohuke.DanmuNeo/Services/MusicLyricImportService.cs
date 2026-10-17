using Newtonsoft.Json.Linq;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 从音乐平台搜索并导入歌词。
/// </summary>
public class MusicLyricImportService
{
    private const int NET_EASE_SUCCESS_CODE = 200;
    private const int QQ_MUSIC_SUCCESS_CODE = 0;
    private readonly HttpClient? httpClient;

    /// <summary>
    /// 创建音乐歌词导入服务；注入的 HTTP 客户端由调用方管理。
    /// </summary>
    public MusicLyricImportService(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient;
    }

    /// <summary>
    /// 搜索音乐平台歌曲。
    /// </summary>
    public async Task<List<MusicLyricSearchResult>> SearchAsync(
        string keyword,
        string source,
        string qqMusicCookie,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(keyword))
        {
            return [];
        }

        return source switch
        {
            "qq" => await SearchQQMusicAsync(keyword.Trim(), qqMusicCookie, cancellationToken),
            _ => await SearchNetEaseAsync(keyword.Trim(), cancellationToken)
        };
    }

    /// <summary>
    /// 导入指定搜索结果的歌词。
    /// </summary>
    public async Task<LyricLibraryItem> ImportAsync(
        MusicLyricSearchResult result,
        string qqMusicCookie,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return result.Source switch
        {
            "qq" => await ImportQQMusicAsync(result, qqMusicCookie, cancellationToken),
            _ => await ImportNetEaseAsync(result, cancellationToken)
        };
    }

    private async Task<List<MusicLyricSearchResult>> SearchNetEaseAsync(
        string keyword,
        CancellationToken cancellationToken)
    {
        using var api = new NetEaseMusicApi(httpClient: httpClient);
        var response = await api.SearchSongsAsync(keyword, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSuccess(response.Code, NET_EASE_SUCCESS_CODE, "网易云音乐搜索", response.ExtraData);
        var songs = response.Result?.Songs ?? throw new InvalidDataException("网易云音乐未返回搜索结果。");

        return songs.Select(song => new MusicLyricSearchResult
        {
            Source = "wy",
            SourceSongId = song.Id.ToString(),
            Title = song.Name,
            Artist = string.Join(" / ", song.Artists.Select(artist => artist.Name).Where(name => !string.IsNullOrWhiteSpace(name))),
            Album = song.Album?.Name ?? ""
        }).ToList();
    }

    private async Task<List<MusicLyricSearchResult>> SearchQQMusicAsync(
        string keyword,
        string qqMusicCookie,
        CancellationToken cancellationToken)
    {
        using var api = new QQMusicApi(qqMusicCookie, httpClient: httpClient);
        var response = await api.SearchSongsV1Async(keyword, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSuccess(response.Code, QQ_MUSIC_SUCCESS_CODE, "QQ 音乐搜索", response.ExtraData);
        var songs = response.Data?.Song?.List ?? throw new InvalidDataException("QQ 音乐未返回搜索结果。");

        return songs.Select(song => new MusicLyricSearchResult
        {
            Source = "qq",
            SourceSongId = song.SongId.ToString(),
            SongMid = song.SongMid,
            Title = song.SongName,
            Artist = string.Join(" / ", song.Singer.Select(singer => singer.Name).Where(name => !string.IsNullOrWhiteSpace(name))),
            Album = song.AlbumName
        }).ToList();
    }

    private async Task<LyricLibraryItem> ImportNetEaseAsync(
        MusicLyricSearchResult result,
        CancellationToken cancellationToken)
    {
        using var api = new NetEaseMusicApi(httpClient: httpClient);
        var songId = long.TryParse(result.SourceSongId, out var parsedSongId) ? parsedSongId : 0;
        var lyric = await api.GetLyricAsync(songId, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSuccess(lyric.Code, NET_EASE_SUCCESS_CODE, "网易云音乐歌词获取", lyric.ExtraData);

        return CreateLibraryItem(
            result,
            lyric.Lyric?.Lyric ?? "",
            lyric.TranslatedLyric?.Lyric ?? "");
    }

    private async Task<LyricLibraryItem> ImportQQMusicAsync(
        MusicLyricSearchResult result,
        string qqMusicCookie,
        CancellationToken cancellationToken)
    {
        using var api = new QQMusicApi(qqMusicCookie, httpClient: httpClient);
        var lyric = await api.GetLyricAsync(result.SongMid, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSuccess(lyric.Code, QQ_MUSIC_SUCCESS_CODE, "QQ 音乐歌词获取", lyric.ExtraData);

        return CreateLibraryItem(
            result,
            lyric.Lyric,
            lyric.TranslatedLyric);
    }

    private static LyricLibraryItem CreateLibraryItem(
        MusicLyricSearchResult result,
        string lyricText,
        string translatedLyricText)
    {
        if (string.IsNullOrWhiteSpace(lyricText) && string.IsNullOrWhiteSpace(translatedLyricText))
        {
            throw new InvalidDataException($"{result.SourceLabel}未返回可用的歌词内容。");
        }

        return new()
        {
            Title = result.Title,
            Artist = result.Artist,
            Album = result.Album,
            Source = result.Source,
            SourceSongId = result.SourceSongId,
            Tags = string.Join(" ", result.Title, result.Artist, result.Album, result.SourceLabel),
            HasTranslation = !string.IsNullOrWhiteSpace(translatedLyricText),
            LyricText = lyricText,
            TranslatedLyricText = translatedLyricText
        };
    }

    private static void EnsureSuccess(int code, int successCode, string operation,
        IDictionary<string, JToken>? extraData)
    {
        if (code == successCode)
        {
            return;
        }

        string? message = null;

        foreach (var key in new[] { "message", "msg" })
        {
            var token = extraData?.FirstOrDefault(pair =>
                string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

            if (token is { Type: JTokenType.String } && !string.IsNullOrWhiteSpace(token.Value<string>()))
            {
                message = token.Value<string>();
                break;
            }
        }

        var reason = string.IsNullOrWhiteSpace(message) ? string.Empty : $"：{message}";
        throw new InvalidOperationException($"{operation}失败（状态码 {code}）{reason}");
    }
}
