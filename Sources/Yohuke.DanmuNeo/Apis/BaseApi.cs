using Yohuke.DanmuNeo.I18n;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using Newtonsoft.Json;

namespace Yohuke.DanmuNeo.Apis;

/// <summary>
/// 为平台 API 提供统一的 HTTP 请求和 JSON 反序列化能力。
/// </summary>
public class BaseApi : IDisposable
{
    /// <summary>
    /// 默认浏览器 User-Agent。
    /// </summary>
    public const string DEFAULT_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/102.0.5005.63 Safari/537.36 Edg/102.0.1245.30";

    private readonly bool disposeClient;

    /// <summary>
    /// 初始化 API 基类。
    /// </summary>
    public BaseApi(HttpClient? httpClient = null, TimeSpan? timeout = null)
    {
        Client = httpClient ?? new();
        disposeClient = httpClient is null;
        DefaultTimeout = timeout ?? TimeSpan.FromSeconds(8);
    }

    /// <summary>
    /// 默认请求超时时间。
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; }

    /// <summary>
    /// HTTP 客户端。
    /// </summary>
    protected HttpClient Client { get; }

    /// <summary>
    /// 释放内部 HTTP 客户端。
    /// </summary>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        if (disposeClient)
        {
            Client.Dispose();
        }
    }

    /// <summary>
    /// 发送 GET 请求并反序列化 JSON。
    /// </summary>
    protected async Task<T> GetJsonAsync<T>(
        string url,
        IReadOnlyDictionary<string, object?>? parameters = null,
        IReadOnlyDictionary<string, string>? headers = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var text = await GetStringAsync(url, parameters, headers, timeout, cancellationToken);
        return Deserialize<T>(text);
    }

    /// <summary>
    /// 发送 GET 请求并返回文本。
    /// </summary>
    protected async Task<string> GetStringAsync(
        string url,
        IReadOnlyDictionary<string, object?>? parameters = null,
        IReadOnlyDictionary<string, string>? headers = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, AppendQuery(url, parameters), headers);
        using var response = await SendAsync(request, timeout, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <summary>
    /// 发送 GET 请求并返回完整响应。
    /// </summary>
    protected async Task<HttpResponseMessage> GetResponseAsync(
        string url,
        IReadOnlyDictionary<string, object?>? parameters = null,
        IReadOnlyDictionary<string, string>? headers = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, AppendQuery(url, parameters), headers);
        return await SendAsync(request, timeout, cancellationToken);
    }

    /// <summary>
    /// 发送表单 POST 请求并反序列化 JSON。
    /// </summary>
    protected async Task<T> PostFormJsonAsync<T>(
        string url,
        IReadOnlyDictionary<string, object?> data,
        IReadOnlyDictionary<string, string>? headers = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, url, headers);
        request.Content = new FormUrlEncodedContent(ToStringPairs(data));
        using var response = await SendAsync(request, timeout, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return Deserialize<T>(text);
    }

    /// <summary>
    /// 发送 JSON POST 请求并反序列化 JSON。
    /// </summary>
    protected async Task<T> PostJsonAsync<T>(
        string url,
        object data,
        IReadOnlyDictionary<string, string>? headers = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, url, headers);
        request.Content = JsonContent.Create(data);
        using var response = await SendAsync(request, timeout, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return Deserialize<T>(text);
    }

    /// <summary>
    /// 发送原始文本 POST 请求并反序列化 JSON。
    /// </summary>
    protected async Task<T> PostTextJsonAsync<T>(
        string url,
        string data,
        IReadOnlyDictionary<string, string>? headers = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Post, url, headers);
        request.Content = new StringContent(data, Encoding.UTF8, "application/json");
        using var response = await SendAsync(request, timeout, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return Deserialize<T>(text);
    }

    /// <summary>
    /// 反序列化 JSON 文本。
    /// </summary>
    protected static T Deserialize<T>(string text)
    {
        var value = JsonConvert.DeserializeObject<T>(text);

        if (value is null)
        {
            throw LocalizedExceptionText.Create("Services.Api.EmptyJsonResult",
                message => new JsonSerializationException(message));
        }

        return value;
    }

    /// <summary>
    /// 创建默认请求头。
    /// </summary>
    protected static Dictionary<string, string> CreateDefaultHeaders()
    {
        return new()
        {
            ["User-Agent"] = DEFAULT_USER_AGENT
        };
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        TimeSpan? timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutTokenSource.CancelAfter(timeout ?? DefaultTimeout);

        var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutTokenSource.Token);
        response.EnsureSuccessStatusCode();
        return response;
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        IReadOnlyDictionary<string, string>? headers)
    {
        var request = new HttpRequestMessage(method, url);
        var mergedHeaders = CreateDefaultHeaders();

        if (headers is not null)
        {
            foreach (var pair in headers)
            {
                mergedHeaders[pair.Key] = pair.Value;
            }
        }

        foreach (var pair in mergedHeaders)
        {
            request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
        }

        return request;
    }

    private static string AppendQuery(string url, IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return url;
        }

        var query = string.Join("&", ToStringPairs(parameters).Select(pair =>
            $"{WebUtility.UrlEncode(pair.Key)}={WebUtility.UrlEncode(pair.Value)}"));
        var separator = url.Contains('?') ? "&" : "?";
        return url + separator + query;
    }

    private static IEnumerable<KeyValuePair<string, string>> ToStringPairs(IReadOnlyDictionary<string, object?> data)
    {
        foreach (var pair in data)
        {
            if (pair.Value is null)
            {
                continue;
            }

            var value = pair.Value switch
            {
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => pair.Value.ToString()
            };

            yield return new(pair.Key, value ?? "");
        }
    }
}
