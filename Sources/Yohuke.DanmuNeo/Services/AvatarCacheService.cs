using Yohuke.DanmuNeo.I18n;
using System.Net.Http;
using System.Security.Cryptography;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Models.Logging;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 提供主播头像拉取与本地缓存能力。
/// </summary>
public class AvatarCacheService : IRoomAvatarService, IDisposable
{
    private static readonly HashSet<string> SUPPORTED_EXTENSIONS = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
        ".gif"
    };

    private readonly AppDirectoryService directoryService;
    private readonly HttpClient httpClient;
    private readonly bool disposeHttpClient;

    /// <summary>
    /// 初始化头像缓存服务。
    /// </summary>
    public AvatarCacheService(AppDirectoryService? directoryService = null, HttpClient? httpClient = null)
    {
        this.directoryService = directoryService ?? new();
        this.httpClient = httpClient ?? new();
        disposeHttpClient = httpClient is null;
    }

    /// <summary>
    /// 释放内部 HTTP 客户端。
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (disposeHttpClient)
        {
            httpClient.Dispose();
        }
    }

    /// <summary>
    /// 获取指定直播间主播头像缓存路径。
    /// </summary>
    public async Task<string> GetRoomAvatarAsync(
        string roomId,
        string? cookie = "",
        CancellationToken cancellationToken = default)
    {
        var result = await GetRoomAvatarResultAsync(roomId, "", cookie, cancellationToken);
        return result.AvatarPath;
    }

    /// <summary>
    /// 获取指定直播间主播信息和头像缓存路径。
    /// </summary>
    public async Task<RoomAvatarResult> GetRoomAvatarResultAsync(
        string roomId,
        string ownerUid = "",
        string? cookie = "",
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var api = new BilibiliApi(cookie ?? "");

            if (long.TryParse(ownerUid.Trim(), out var parsedOwnerUid))
            {
                var userResult =
                    await GetUserAvatarResultAsync(api, parsedOwnerUid, ownerUid.Trim(), cancellationToken);

                if (!string.IsNullOrWhiteSpace(userResult.AvatarPath))
                {
                    return userResult;
                }
            }

            if (!long.TryParse(roomId, out var parsedRoomId))
            {
                return new();
            }

            var response = await api.GetRoomInfoAsync(parsedRoomId, cancellationToken: cancellationToken);
            var resolvedOwnerUid = response.Data?.RoomInfo?.Uid > 0
                ? response.Data.RoomInfo.Uid.ToString()
                : ownerUid.Trim();
            var cacheKey = string.IsNullOrWhiteSpace(resolvedOwnerUid) ? roomId : resolvedOwnerUid;
            var avatarUrl = response.Data?.AnchorInfo?.BaseInfo?.Face;
            var avatarPath = await DownloadAvatarAsync(cacheKey, avatarUrl, cancellationToken);

            return new()
            {
                OwnerUid = resolvedOwnerUid,
                OwnerName = response.Data?.AnchorInfo?.BaseInfo?.Name ?? "",
                AvatarPath = avatarPath
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            StartupLog.Append(Localization.Format("Services.Avatar.FetchFailed", roomId), AppLogLevel.ERROR, Localization.Get("Services.Avatar.Source"), exception);
            return new();
        }
    }

    private async Task<RoomAvatarResult> GetUserAvatarResultAsync(
        BilibiliApi api,
        long parsedOwnerUid,
        string ownerUid,
        CancellationToken cancellationToken)
    {
        try
        {
            var cardResponse =
                await api.GetUserCardContainerAsync(parsedOwnerUid, cancellationToken: cancellationToken);
            var card = cardResponse.Data?.Card;
            var avatarPath = await DownloadAvatarAsync(ownerUid, card?.Face, cancellationToken);

            if (!string.IsNullOrWhiteSpace(avatarPath))
            {
                return new()
                {
                    OwnerUid = ownerUid,
                    OwnerName = card?.Name ?? "",
                    AvatarPath = avatarPath
                };
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            StartupLog.Append(Localization.Format("Services.Avatar.PrimaryFailed", ownerUid), AppLogLevel.WARNING, Localization.Get("Services.Avatar.Source"), exception);
        }

        try
        {
            var userResponse = await api.GetUserCardAsync(parsedOwnerUid, cancellationToken: cancellationToken);
            var avatarPath = await DownloadAvatarAsync(ownerUid, userResponse.Data?.Face, cancellationToken);

            if (!string.IsNullOrWhiteSpace(avatarPath))
            {
                return new()
                {
                    OwnerUid = ownerUid,
                    OwnerName = userResponse.Data?.Name ?? "",
                    AvatarPath = avatarPath
                };
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            StartupLog.Append(Localization.Format("Services.Avatar.FallbackFailed", ownerUid), AppLogLevel.WARNING, Localization.Get("Services.Avatar.Source"), exception);
        }

        return new();
    }

    /// <summary>
    /// 下载头像并返回缓存文件路径。
    /// </summary>
    public async Task<string> DownloadAvatarAsync(
        string roomId,
        string? avatarUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(roomId) ||
            string.IsNullOrWhiteSpace(avatarUrl) ||
            !Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri))
        {
            return "";
        }

        directoryService.EnsureDirectories();
        var filePath = GetAvatarFilePath(roomId, avatarUrl);

        if (File.Exists(filePath))
        {
            return filePath;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.TryAddWithoutValidation("User-Agent", BaseApi.DEFAULT_USER_AGENT);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            if (bytes.Length == 0)
            {
                return "";
            }

            await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
            DeleteOldRoomAvatarFiles(roomId, filePath);
            return filePath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            StartupLog.Append(Localization.Format("Services.Avatar.DownloadFailed", roomId), AppLogLevel.WARNING, Localization.Get("Services.Avatar.Source"), exception);
            return "";
        }
    }

    /// <summary>
    /// 获取头像缓存文件路径。
    /// </summary>
    public string GetAvatarFilePath(string roomId, string avatarUrl)
    {
        return Path.Combine(directoryService.AvatarCacheDirectory, CreateCacheFileName(roomId, avatarUrl));
    }

    /// <summary>
    /// 创建稳定的头像缓存文件名。
    /// </summary>
    public static string CreateCacheFileName(string roomId, string avatarUrl)
    {
        var normalizedRoomId = NormalizeFileNamePart(roomId);
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(avatarUrl)))[..12]
            .ToLowerInvariant();
        var extension = GetSafeExtension(avatarUrl);
        return $"{normalizedRoomId}-{hash}{extension}";
    }

    private void DeleteOldRoomAvatarFiles(string roomId, string currentFilePath)
    {
        var prefix = $"{NormalizeFileNamePart(roomId)}-";

        foreach (var filePath in Directory.EnumerateFiles(directoryService.AvatarCacheDirectory, $"{prefix}*"))
        {
            if (string.Equals(filePath, currentFilePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                File.Delete(filePath);
            }
            catch (Exception exception)
            {
                StartupLog.Append(Localization.Get("Services.Avatar.CleanupFailed"), AppLogLevel.WARNING, Localization.Get("Services.Avatar.Source"), exception);
            }
        }
    }

    private static string NormalizeFileNamePart(string value)
    {
        var chars = value
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .ToArray();
        return chars.Length == 0 ? "room" : new(chars);
    }

    private static string GetSafeExtension(string avatarUrl)
    {
        if (!Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri))
        {
            return ".jpg";
        }

        var extension = Path.GetExtension(uri.LocalPath);
        return SUPPORTED_EXTENSIONS.Contains(extension) ? extension.ToLowerInvariant() : ".jpg";
    }
}
