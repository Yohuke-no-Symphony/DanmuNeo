namespace Yohuke.DanmuNeo.Services;

/// <summary>仅携带进程内播放器身份的原生网页截图动作。</summary>
public sealed class LiveCaptureShortcutEventArgs(string token) : EventArgs
{
    public string Token { get; } = token;
}
