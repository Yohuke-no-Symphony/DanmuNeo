namespace Yohuke.DanmuNeo.Apis;

/// <summary>已携带账号身份的接口明确返回未登录。</summary>
public sealed class BilibiliAuthenticationException(string message) : InvalidOperationException(message)
{
}
