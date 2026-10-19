using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels;

public partial class MainWindowViewModel
{
    private readonly LiveRoomReceptionServices receptionServices;
    private readonly IReceivedDanmuArchiveService receivedDanmuArchiveService;
    private readonly bool ownsReceivedDanmuArchiveService;
    private Task? receptionShutdownTask;

    /// <summary>是否将监听收到的直播消息自动留档。</summary>
    public bool ArchiveReceivedDanmu
    {
        get => Settings.ArchiveReceivedDanmu;
        set
        {
            if (Settings.ArchiveReceivedDanmu == value)
            {
                return;
            }

            Settings.ArchiveReceivedDanmu = value;
            OnPropertyChanged();
            foreach (var room in Workspaces.SelectMany(workspace => workspace.Rooms))
            {
                room.RefreshDanmuArchiving();
            }

            _ = SaveArchivePreferenceAsync();
        }
    }

    /// <summary>打开独立于缓存及配置备份的弹幕留档目录。</summary>
    public void OpenDanmuLogsDirectory()
    {
        OpenDirectory(appDirectoryService.DanmuLogsDirectory, Localization.Get("Room.Archive.OpenFailed"));
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await ShutdownReceptionAsync();
        Dispose();
    }

    private Task ShutdownReceptionAsync()
    {
        return receptionShutdownTask ??= CompleteReceptionAsync();
    }

    private async Task CompleteReceptionAsync()
    {
        DisposeTools();
        await medalSwitchCoordinator.DisposeAsync();
        foreach (var room in Workspaces.SelectMany(workspace => workspace.Rooms))
        {
            room.Dispose();
        }

        if (ownsReceivedDanmuArchiveService)
        {
            await receivedDanmuArchiveService.DisposeAsync();
        }
        else
        {
            await receivedDanmuArchiveService.FlushAsync();
        }
    }

    private async Task SaveArchivePreferenceAsync()
    {
        try
        {
            await SaveAsync();
        }
        catch (Exception exception)
        {
            LogErrorOnce(Localization.Get("Room.Archive.Source"), Localization.Get("App.Config.SaveFailed"), exception);
        }
    }
}
