using Yohuke.DanmuNeo.I18n;

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
        LiveMarker.Dispose();
        sendLifetimeTokenSource.Cancel();
        lyricSessionTokenSource.Cancel();
        draftDeliveryProgress = null;
        lyricDeliveryProgress.Clear();
        Localization.LanguageChanged -= OnLanguageChanged;
        Deactivate();
        StopAutoLyric(false);
        StopListening();
        DisposeForwardRules();
        InvalidateDraftSendFeedback();

        if (sendService is not null)
        {
            sendService.RecordCreated -= OnSendRecordCreated;
        }

        sendLifetimeTokenSource.Dispose();
        lyricSessionTokenSource.Dispose();
        avatarImage?.Dispose();
        avatarImage = null;
        GC.SuppressFinalize(this);
    }
}
