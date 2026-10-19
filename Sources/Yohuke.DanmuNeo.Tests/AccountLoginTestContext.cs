using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Services;
using Yohuke.DanmuNeo.ViewModels;

namespace Yohuke.DanmuNeo.Tests;

internal sealed class AccountLoginTestContext : IAsyncDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "DanmuNeo-account-login-" + Guid.NewGuid().ToString("N"));

    internal AccountLoginTestContext(params BilibiliAccount[] accounts)
    {
        StateService = new(directory);
        StateService.Save(new() { Accounts = [.. accounts] });
        Sessions = new(Validation);
        Application = new(StateService, new FakeLiveRoomSearchService(), new FakeRoomAvatarService(),
            accountSessions: Sessions);
    }

    internal ControlledAccountValidationService Validation { get; } = new();
    internal AppStateService StateService { get; }
    internal BilibiliAccountSessionService Sessions { get; }
    internal MainWindowViewModel Application { get; }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await Application.PendingAccountNameSave;
        await Application.DisposeAsync();
        Sessions.Dispose();
        Directory.Delete(directory, true);
    }
}
