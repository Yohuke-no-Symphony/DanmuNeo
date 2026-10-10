using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 每次监听使用独立连接的离线测试工厂。
/// </summary>
public class SequenceLiveDanmuSocketFactory : ILiveDanmuSocketFactory
{
    private readonly Queue<FakeLiveDanmuSocket> sockets;

    /// <summary>
    /// 按顺序提供测试连接。
    /// </summary>
    public SequenceLiveDanmuSocketFactory(params FakeLiveDanmuSocket[] sockets)
    {
        this.sockets = new(sockets);
    }

    /// <inheritdoc/>
    public ILiveDanmuSocket Create(long roomId, string cookie)
    {
        return sockets.Dequeue();
    }
}
