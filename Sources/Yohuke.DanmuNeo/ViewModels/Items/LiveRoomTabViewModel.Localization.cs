namespace Yohuke.DanmuNeo.ViewModels.Items;

public partial class LiveRoomTabViewModel
{
    private LocalizedMessage connectionStatusMessage = new("Room.Disconnected");
    private LocalizedMessage draftSendStatusMessage = new("");
    private LocalizedMessage draftSendErrorMessage = new("");
    private LocalizedMessage danmuConfigurationStatusMessage = new("Room.ConfigurationHint");
    private LocalizedMessage moderationStatusMessage = new("");

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ConnectionStatus = connectionStatusMessage.Render();
        DraftSendStatus = draftSendStatusMessage.Render();
        DraftSendError = draftSendErrorMessage.Render();
        DanmuConfigurationStatus = danmuConfigurationStatusMessage.Render();
        ModerationStatus = moderationStatusMessage.Render();
        OnPropertyChanged(nameof(LyricAutoSendingToggleToolTip));
        OnPropertyChanged(nameof(ListenToggleText));
        OnPropertyChanged(nameof(LivePlayerToggleToolTip));
        OnPropertyChanged(nameof(CurrentLyricTitleText));

        foreach (var option in LyricSendModeOptions)
        {
            option.RefreshLocalization();
        }

        foreach (var item in DanmuItems.Concat(TranslateHistoryItems).Distinct())
        {
            item.RefreshLocalization();
        }

        foreach (var item in SuperChats)
        {
            item.RefreshLocalization();
        }
    }

    private void SetConnectionStatus(string key, params object[] arguments)
    {
        connectionStatusMessage = new(key, arguments);
        ConnectionStatus = connectionStatusMessage.Render();
    }

    private void SetDraftSendStatus(LocalizedMessage message)
    {
        draftSendStatusMessage = message;
        DraftSendStatus = message.Render();
    }

    private void SetDanmuConfigurationStatus(string key, params object[] arguments)
    {
        danmuConfigurationStatusMessage = new(key, arguments);
        DanmuConfigurationStatus = danmuConfigurationStatusMessage.Render();
    }

    private void SetModerationStatus(string key, params object[] arguments)
    {
        moderationStatusMessage = new(key, arguments);
        ModerationStatus = moderationStatusMessage.Render();
    }
}
