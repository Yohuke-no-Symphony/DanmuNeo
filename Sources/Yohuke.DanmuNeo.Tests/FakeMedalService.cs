using System.Threading;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class FakeMedalService : IBilibiliMedalService
{
    internal AccountMedalCollection Collection { get; set; } = new([
        new(11, 100, "牌一", 10, false), new(22, 200, "牌二", 20, true), new(33, 300, "牌三", 30, false)
    ], true);
    internal List<string> Writes { get; } = [];
    internal int ReadCount { get; private set; }
    internal int OwnerReadCount { get; private set; }
    internal long OwnerUid { get; set; } = 100;
    internal Exception? ReadError { get; set; }
    internal TaskCompletionSource? FirstWriteStarted { get; set; }
    internal TaskCompletionSource? ReleaseFirstWrite { get; set; }
    internal TaskCompletionSource? ReadStarted { get; set; }
    internal TaskCompletionSource? ReleaseRead { get; set; }
    internal TaskCompletionSource? OwnerReadStarted { get; set; }
    internal TaskCompletionSource? ReleaseOwnerRead { get; set; }

    /// <inheritdoc/>
    public async Task<long> GetRoomOwnerUidAsync(string roomId, string? cookie = null, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        OwnerReadCount++;
        OwnerReadStarted?.TrySetResult();
        if (ReleaseOwnerRead is not null)
        {
            await ReleaseOwnerRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        return OwnerUid;
    }

    /// <inheritdoc/>
    public async Task<AccountMedalCollection> GetMedalsAsync(long accountUid, string cookie, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ReadCount++;
        ReadStarted?.TrySetResult();
        if (ReleaseRead is not null)
        {
            await ReleaseRead.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        if (ReadError is not null)
        {
            throw ReadError;
        }
        return Collection;
    }

    /// <inheritdoc/>
    public Task WearAsync(long medalId, string cookie, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) => WriteAsync(medalId, false);

    /// <inheritdoc/>
    public Task TakeOffAsync(long medalId, string cookie, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) => WriteAsync(medalId, true);

    private async Task WriteAsync(long id, bool takeOff)
    {
        Writes.Add($"{(takeOff ? "off" : "wear")}:{id}");
        if (Writes.Count == 1 && ReleaseFirstWrite is not null)
        {
            FirstWriteStarted?.TrySetResult();
            await ReleaseFirstWrite.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Collection = Collection with { Items = Collection.Items.Select(item => item with { IsWearing = !takeOff && item.Id == id }).ToList() };
    }
}
