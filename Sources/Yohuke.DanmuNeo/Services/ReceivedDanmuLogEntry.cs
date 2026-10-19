namespace Yohuke.DanmuNeo.Services;

/// <summary>独立于界面对象的收到消息留档快照。</summary>
public sealed record ReceivedDanmuLogEntry(string Kind, string UserUid, string UserName, string Content,
    DateTimeOffset ReceivedAt, long ServerTimestamp = 0, bool IsHistory = false);
