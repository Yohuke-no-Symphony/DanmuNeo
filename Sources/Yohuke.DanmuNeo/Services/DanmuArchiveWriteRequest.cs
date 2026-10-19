namespace Yohuke.DanmuNeo.Services;

internal sealed record DanmuArchiveWriteRequest(DanmuArchiveSession? Session, ReceivedDanmuLogEntry? Entry,
    TaskCompletionSource? Completion = null);
