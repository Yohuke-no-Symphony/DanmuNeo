using System.Threading;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class ScriptedDanmuSendTransport : IDanmuSendTransport
{
    public List<string> Messages { get; } = [];
    public Queue<DanmuSendAttemptResult> Results { get; } = [];
    public Action? BeforeResponse { get; set; }

    public Task<DanmuSendAttemptResult> SendOnceAsync(string roomId, string message, string cookie,
        DanmuRoomConfig config, TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Messages.Add(message);
        BeforeResponse?.Invoke();
        return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : new());
    }
}
