using Yohuke.DanmuNeo.I18n;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Apis.Models.Common;

namespace Yohuke.DanmuNeo.Apis;

/// <summary>
/// 生成 B 站 WBI 签名参数。
/// </summary>
public class BilibiliWbiSigner : BaseApi
{
    private static readonly int[] MIXIN_KEY_ENC_TAB =
    [
        46, 47, 18, 2, 53, 8, 23, 32, 15, 50, 10, 31, 58, 3, 45, 35,
        27, 43, 5, 49, 33, 9, 42, 19, 29, 28, 14, 39, 12, 38, 41, 13,
        37, 48, 7, 16, 24, 55, 40, 61, 26, 17, 0, 1, 60, 51, 30, 4, 22,
        25, 54, 21, 56, 59, 6, 63, 57, 62, 11, 36, 20, 34, 44, 52
    ];

    private string imgKey = "";
    private string subKey = "";
    private DateTimeOffset nextRefreshTime = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    /// <summary>
    /// 初始化 WBI 签名器。
    /// </summary>
    public BilibiliWbiSigner(TimeSpan? timeout = null)
        : base(timeout: timeout)
    {
    }

    /// <summary>
    /// 为参数补充 w_rid 与 wts。
    /// </summary>
    public async Task<Dictionary<string, object?>> FillAsync(
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        await EnsureKeyAsync(cancellationToken);

        var result = new Dictionary<string, object?>(parameters);
        var wts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        result["wts"] = wts;
        var mixinKey = GetMixinKey(imgKey, subKey);
        var source = string.Join("&", result.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            $"{pair.Key}={FormatValue(pair.Value)}")) + mixinKey;
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(source));
        result["w_rid"] = Convert.ToHexString(hash).ToLowerInvariant();
        return result;
    }

    /// <summary>
    /// 生成 mixin key。
    /// </summary>
    public static string GetMixinKey(string imgKey, string subKey)
    {
        var key = imgKey + subKey;
        var builder = new StringBuilder();

        foreach (var index in MIXIN_KEY_ENC_TAB.Take(32))
        {
            if (index < key.Length)
            {
                builder.Append(key[index]);
            }
        }

        return builder.ToString();
    }

    private async Task EnsureKeyAsync(CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < nextRefreshTime && !string.IsNullOrEmpty(imgKey) && !string.IsNullOrEmpty(subKey))
        {
            return;
        }

        await refreshLock.WaitAsync(cancellationToken);

        try
        {
            if (DateTimeOffset.UtcNow < nextRefreshTime && !string.IsNullOrEmpty(imgKey) && !string.IsNullOrEmpty(subKey))
            {
                return;
            }

            var response = await GetJsonAsync<ApiResponse<BilibiliWbiImageData>>(
                "https://api.bilibili.com/x/web-interface/nav",
                headers: CreateDefaultHeaders(),
                cancellationToken: cancellationToken);
            var image = response.Data?.WbiImage ?? throw LocalizedExceptionText.Create("Services.BilibiliWbi.ImageKeysMissing",
                message => new InvalidOperationException(message));
            imgKey = ExtractKey(image.ImgUrl);
            subKey = ExtractKey(image.SubUrl);
            nextRefreshTime = DateTimeOffset.UtcNow.AddDays(1);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private static string ExtractKey(string url)
    {
        var fileName = url.Split('/').LastOrDefault() ?? "";
        var dotIndex = fileName.IndexOf('.');
        return dotIndex >= 0 ? fileName[..dotIndex] : fileName;
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };
    }
}
