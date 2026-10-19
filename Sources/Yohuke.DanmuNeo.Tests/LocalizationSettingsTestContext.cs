using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class LocalizationSettingsTestContext : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(),
        "DanmuNeo-localization-settings-" + Guid.NewGuid().ToString("N"));
    private readonly BilibiliAccountSessionService sessions = new(new MedalValidationService());

    internal LocalizationSettingsTestContext(string languageId)
    {
        StateService = new(directory);
        StateService.Save(new() { Settings = new() { Language = languageId } });
        var state = StateService.Load();
        AppLanguageService.ApplyPreference(state.Settings.Language);
        ViewModel = new(StateService, new FakeLiveRoomSearchService(), new FakeRoomAvatarService(),
            danmuConfigService: new FakeBilibiliDanmuConfigService(), loadedState: state, accountSessions: sessions);
    }

    internal AppStateService StateService { get; }
    internal MainWindowViewModel ViewModel { get; }

    /// <inheritdoc/>
    public void Dispose()
    {
        ViewModel.Dispose();
        sessions.Dispose();
        Directory.Delete(directory, true);
    }
}
