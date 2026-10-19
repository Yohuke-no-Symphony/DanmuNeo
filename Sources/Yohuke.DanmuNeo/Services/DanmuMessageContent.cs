namespace Yohuke.DanmuNeo.Services;

/// <summary>带有独立标识符的单语言发送正文。</summary>
public sealed record DanmuMessageContent
{
    /// <summary>正文的语言或业务标识。</summary>
    public string Language { get; init; } = "original";
    /// <summary>替换规则处理后的正文。</summary>
    public string Body { get; init; } = "";
    /// <summary>每个分段的前缀。</summary>
    public string Prefix { get; init; } = "";
    /// <summary>每个分段的后缀。</summary>
    public string Suffix { get; init; } = "";
}
