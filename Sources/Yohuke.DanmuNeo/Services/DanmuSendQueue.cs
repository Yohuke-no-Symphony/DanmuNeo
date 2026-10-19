namespace Yohuke.DanmuNeo.Services;

/// <summary>在请求进入时确定全局顺序，取消等待项不会越过先前请求。</summary>
internal sealed class DanmuSendQueue
{
    private readonly object gate = new();
    private Task tail = Task.CompletedTask;

    internal DanmuSendQueueTicket Enqueue()
    {
        lock (gate)
        {
            var ticket = new DanmuSendQueueTicket(tail);
            tail = ticket.Completion;
            return ticket;
        }
    }
}
