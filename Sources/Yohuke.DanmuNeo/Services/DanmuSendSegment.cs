namespace Yohuke.DanmuNeo.Services;

/// <summary>保留完整符号与正文范围的实际弹幕分段。</summary>
public sealed record DanmuSendSegment
{
    /// <summary>正文中的 UTF-16 起点。</summary>
    public int BodyStart { get; init; }
    /// <summary>该段占用的正文 UTF-16 长度。</summary>
    public int BodyLength { get; init; }
    /// <summary>实际发送文本。</summary>
    public string Message { get; init; } = "";
}
