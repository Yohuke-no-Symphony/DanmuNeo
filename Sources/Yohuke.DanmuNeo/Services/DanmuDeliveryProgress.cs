using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.Services;

/// <summary>单次逻辑发送已接受的语言正文范围和最终分段结果。</summary>
public sealed class DanmuDeliveryProgress
{
    private readonly string accountId;
    private readonly string cookie;
    private readonly int[] acceptedLengths;
    private readonly Dictionary<(int ContentIndex, int Start), DanmuSendResult> results = [];

    /// <summary>创建与内容及身份快照绑定的投递进度。</summary>
    public DanmuDeliveryProgress(DanmuSendRequest request, BilibiliAccount? account)
    {
        Request = request with { Contents = request.Contents.Select(content => content with { }).ToArray() };
        accountId = account?.Id ?? "";
        cookie = account?.Cookie ?? "";
        acceptedLengths = new int[Request.Contents.Count];
    }

    /// <summary>不包含账号凭据的稳定操作标识。</summary>
    public string OperationId { get; } = Guid.NewGuid().ToString("N");
    /// <summary>此操作的不可变内容快照。</summary>
    public DanmuSendRequest Request { get; }
    /// <summary>所有正文是否已经被接口接受。</summary>
    public bool IsComplete => Request.Contents.Count > 0 && Request.Contents.Select((content, index) =>
        acceptedLengths[index] == content.Body.Length).All(complete => complete);
    /// <summary>按语言和正文范围排列的分段终态。</summary>
    public IReadOnlyList<DanmuSendResult> Results => results.OrderBy(pair => pair.Key.ContentIndex)
        .ThenBy(pair => pair.Key.Start).Select(pair => pair.Value).ToArray();

    /// <summary>读取一个语言正文中已经接受的连续前缀长度。</summary>
    public int GetAcceptedLength(int contentIndex)
    {
        return acceptedLengths[contentIndex];
    }

    /// <summary>判断内容、符号、目标和账号凭据是否允许继续原操作。</summary>
    public bool Matches(DanmuSendRequest request, BilibiliAccount? account)
    {
        return Request.RoomId == request.RoomId && accountId == (account?.Id ?? "") &&
               cookie == (account?.Cookie ?? "") && Request.Contents.SequenceEqual(request.Contents);
    }

    internal string GetRecordId(int contentIndex, int start)
    {
        return $"{OperationId}:{contentIndex}:{start}";
    }

    internal void Record(int contentIndex, DanmuSendSegment segment, DanmuSendResult result)
    {
        results[(contentIndex, segment.BodyStart)] = result;
        if (result.IsAccepted)
        {
            acceptedLengths[contentIndex] = segment.BodyStart + segment.BodyLength;
        }
    }
}
