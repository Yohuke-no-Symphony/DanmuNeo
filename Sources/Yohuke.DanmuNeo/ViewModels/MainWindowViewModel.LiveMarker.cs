using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.LiveMarker;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels.Items;

namespace Yohuke.DanmuNeo.ViewModels;

public partial class MainWindowViewModel
{
    private readonly LiveMarkerServices liveMarkerServices;
    private readonly bool ownsLiveMarkerServices;
    private readonly SemaphoreSlim liveMarkerLeaveGate = new(1, 1);

    /// <summary>由工作台注入的未保存文档选择入口。</summary>
    public Func<LiveMarkerViewModel, Task<LiveMarkerLeaveDecision>>? ConfirmLiveMarkerLeaveAsync { get; set; }

    /// <summary>由工作台注入的当前表格编辑提交入口。</summary>
    public Func<bool>? CommitLiveMarkerEditing { get; set; }

    /// <summary>逐一预检记录文档，全部通过后调用方才可以删除或释放房间。</summary>
    public async Task<bool> PrepareLiveMarkersToLeaveAsync(IEnumerable<LiveRoomTabViewModel> rooms)
    {
        await liveMarkerLeaveGate.WaitAsync();
        try
        {
            if (CommitLiveMarkerEditing?.Invoke() == false)
            {
                return false;
            }

            foreach (var room in rooms.ToArray())
            {
                var accepted = await room.LiveMarker.PrepareToLeaveAsync(viewModel =>
                    ConfirmLiveMarkerLeaveAsync?.Invoke(viewModel) ?? Task.FromResult(LiveMarkerLeaveDecision.Cancel));
                if (!accepted)
                {
                    return false;
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            SetLocalizedProperty(nameof(StatusMessage), "LiveMarker.LeaveFailed");
            LogErrorOnce(Localization.Get("LiveMarker.LogSource"), StatusMessage, exception);
            return false;
        }
        finally
        {
            liveMarkerLeaveGate.Release();
        }
    }

    internal async Task CompleteLiveMarkerWritesAsync()
    {
        foreach (var room in Workspaces.SelectMany(workspace => workspace.Rooms).ToArray())
        {
            await room.LiveMarker.CompletePendingWritesAsync();
        }
    }
}
