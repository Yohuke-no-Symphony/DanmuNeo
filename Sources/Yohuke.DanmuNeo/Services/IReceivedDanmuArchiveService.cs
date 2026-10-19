namespace Yohuke.DanmuNeo.Services;

/// <summary>按监听会话异步保存收到的直播消息。</summary>
public interface IReceivedDanmuArchiveService : IAsyncDisposable
{
    /// <summary>创建不包含凭据的留档会话。</summary>
    DanmuArchiveSession OpenSession(string roomId, string workspaceId, string roomStateId);
    /// <summary>非阻塞入队；失败会写入应用错误日志。</summary>
    bool TryAppend(DanmuArchiveSession session, ReceivedDanmuLogEntry entry);
    /// <summary>停止接受该会话新记录，并等待已经接受的记录落盘。</summary>
    Task CompleteSessionAsync(DanmuArchiveSession session);
    /// <summary>等待之前接受的记录刷盘。</summary>
    Task FlushAsync();
}
