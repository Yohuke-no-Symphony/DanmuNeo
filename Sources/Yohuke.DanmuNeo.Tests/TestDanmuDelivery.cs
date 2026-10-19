using System.Threading;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

internal static class TestDanmuDelivery
{
    internal static async Task<DanmuDeliveryResult> SendAsync(DanmuSendRequest request, BilibiliAccount? account,
        AppSettings settings, DanmuDeliveryProgress? continuation, CancellationToken cancellationToken,
        Func<string, string, BilibiliAccount?, AppSettings, CancellationToken, Task<DanmuSendResult>> send,
        Action<DanmuFeedItem> publish, List<string>? parts = null)
    {
        var progress = continuation is not null && continuation.Matches(request, account)
            ? continuation
            : new DanmuDeliveryProgress(request, account);
        for (var index = 0; index < request.Contents.Count; index++)
        {
            var content = request.Contents[index];
            var start = progress.GetAcceptedLength(index);
            if (start >= content.Body.Length)
            {
                continue;
            }
            foreach (var part in parts ?? [content.Prefix + content.Body[start..] + content.Suffix])
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await send(request.RoomId, part, account, settings, cancellationToken);
                result.Record.SendRecordId = progress.GetRecordId(index, start);
                var segment = new DanmuSendSegment { BodyStart = start, BodyLength = content.Body.Length - start, Message = part };
                progress.Record(index, segment, result);
                publish(result.Record);
                if (!result.IsAccepted)
                {
                    return new() { Progress = progress };
                }
                start += segment.BodyLength;
            }
        }
        return new() { Progress = progress };
    }
}
