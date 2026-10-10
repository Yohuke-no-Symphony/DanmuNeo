using System.Threading;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 测试用直播弹幕连接。
/// </summary>
public class FakeLiveDanmuSocket : ILiveDanmuSocket, IDisposable
{
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 后台监听开始的通知。
    /// </summary>
    public Task StartedTask => started.Task;

    /// <summary>
    /// 后台连接释放的通知。
    /// </summary>
    public Task DisposedTask => disposed.Task;

    public event EventHandler<BilibiliDanmuMessage>? DanmuReceived;

    public event EventHandler<BilibiliSuperChatMessage>? SuperChatReceived;

    public event EventHandler<BilibiliGuardPurchaseMessage>? GuardPurchaseReceived;

    public event EventHandler<Exception>? ErrorReceived;

    public event EventHandler? Disconnected;

    public event EventHandler? Recovered;

    public BilibiliDanmuMessage? NextMessage { get; set; }

    public bool KeepRunning { get; set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        started.TrySetResult();
        if (NextMessage is not null)
        {
            DanmuReceived?.Invoke(this, NextMessage);
        }

        if (KeepRunning)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    /// <summary>
    /// 触发普通弹幕消息事件。
    /// </summary>
    public void EmitDanmu(BilibiliDanmuMessage message)
    {
        DanmuReceived?.Invoke(this, message);
    }

    /// <summary>
    /// 触发 SC 消息事件。
    /// </summary>
    public void EmitSuperChat(BilibiliSuperChatMessage message)
    {
        SuperChatReceived?.Invoke(this, message);
    }

    /// <summary>
    /// 触发大航海购买消息事件。
    /// </summary>
    public void EmitGuardPurchase(BilibiliGuardPurchaseMessage message)
    {
        GuardPurchaseReceived?.Invoke(this, message);
    }

    /// <summary>
    /// 触发连接错误事件。
    /// </summary>
    public void EmitError(Exception exception)
    {
        ErrorReceived?.Invoke(this, exception);
    }

    /// <summary>
    /// 触发断线事件。
    /// </summary>
    public void EmitDisconnected()
    {
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 触发恢复事件。
    /// </summary>
    public void EmitRecovered()
    {
        Recovered?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        disposed.TrySetResult();
    }
}
