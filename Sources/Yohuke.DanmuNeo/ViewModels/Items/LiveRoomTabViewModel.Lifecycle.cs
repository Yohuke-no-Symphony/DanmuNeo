namespace Yohuke.DanmuNeo.ViewModels.Items;

public partial class LiveRoomTabViewModel
{
    private bool isDisposed;

    /// <summary>
    /// 离开当前房间，停止播放和发送配置交互，保留后台监听、歌词与转发。
    /// </summary>
    public void Deactivate()
    {
        StopLivePlayer();
        DeactivateDanmuConfiguration();
        InvalidateModerationPermission();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        Deactivate();
        StopAutoLyric(false);
        StopListening();
        DisposeForwardRules();
        InvalidateDraftSendFeedback();

        if (sendService is not null)
        {
            sendService.RecordCreated -= OnSendRecordCreated;
        }

        avatarImage?.Dispose();
        avatarImage = null;
        GC.SuppressFinalize(this);
    }
}
