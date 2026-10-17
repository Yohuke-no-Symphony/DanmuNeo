using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels.Items;

public partial class LiveRoomTabViewModel
{
    private readonly object livePlayerGate = new();
    private CancellationTokenSource? livePlayerRequestSource;
    private ILivePlayerService? livePlayerSessionService;
    private long livePlayerRequestVersion;

    private async Task RunLivePlayerRequestAsync(bool createPlayer)
    {
        var streamService = liveStreamService;
        var playerService = livePlayerService;

        if (streamService is null || createPlayer && playerService is null)
        {
            log?.Error(CreateLogSource(Localization.Get("Room.Log.Player")), Localization.Get("Room.Log.PlayerUnavailable"));
            return;
        }

        CancellationTokenSource tokenSource;
        long requestVersion;

        lock (livePlayerGate)
        {
            if (isDisposed)
            {
                return;
            }

            InvalidateLivePlayerRequest();
            tokenSource = new();
            livePlayerRequestSource = tokenSource;
            requestVersion = livePlayerRequestVersion;
        }

        var cancellationToken = tokenSource.Token;
        var roomId = RoomId;
        var logSource = CreateLogSource(Localization.Get("Room.Log.Player"), roomId);

        try
        {
            var quality = SelectedLiveQuality?.Quality ?? 0;
            var cookie = resolveAccount?.Invoke(this)?.Cookie;
            var settings = getSettings?.Invoke();
            var timeout = settings is null ? null : (TimeSpan?)TimeSpan.FromSeconds(settings.TimeoutSeconds);
            var source = await streamService.GetPlayableSourceAsync(roomId, quality, cookie, timeout, cancellationToken);

            lock (livePlayerGate)
            {
                if (!IsCurrentLivePlayerRequest(tokenSource, requestVersion, roomId))
                {
                    return;
                }

                RefreshLiveQualityOptions(source.QualityOptions, source.CurrentQuality);

                if (!createPlayer || !IsCurrentLivePlayerRequest(tokenSource, requestVersion, roomId))
                {
                    return;
                }
            }

            var session = await playerService!.CreatePlayerAsync(source, cookie, cancellationToken);

            lock (livePlayerGate)
            {
                if (!IsCurrentLivePlayerRequest(tokenSource, requestVersion, roomId))
                {
                    playerService.Revoke(session.Token);
                    return;
                }

                var previousToken = livePlayerToken;
                var previousService = livePlayerSessionService;
                livePlayerToken = session.Token;
                livePlayerSessionService = playerService;
                previousService?.Revoke(previousToken);
                LivePlayerUrl = session.PlayerUrl;

                if (!IsCurrentLivePlayerRequest(tokenSource, requestVersion, roomId))
                {
                    return;
                }

                IsLivePlayerVisible = true;

                if (IsCurrentLivePlayerRequest(tokenSource, requestVersion, roomId))
                {
                    StartListeningWithLivePlayerIfNeeded();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            lock (livePlayerGate)
            {
                if (IsCurrentLivePlayerRequest(tokenSource, requestVersion, roomId))
                {
                    log?.Error(logSource, createPlayer ? Localization.Get("Room.Log.PlayerFailed") : Localization.Get("Room.Log.QualityFailed"), exception);

                    if (string.IsNullOrWhiteSpace(livePlayerToken))
                    {
                        IsLivePlayerVisible = false;
                    }
                }
            }
        }
        finally
        {
            lock (livePlayerGate)
            {
                if (ReferenceEquals(livePlayerRequestSource, tokenSource))
                {
                    livePlayerRequestSource = null;
                }

                tokenSource.Dispose();
            }
        }
    }

    private bool IsCurrentLivePlayerRequest(CancellationTokenSource tokenSource, long version, string roomId)
    {
        return !isDisposed && !tokenSource.IsCancellationRequested &&
               ReferenceEquals(livePlayerRequestSource, tokenSource) && livePlayerRequestVersion == version &&
               RoomId == roomId;
    }

    private void InvalidateLivePlayerRequest()
    {
        livePlayerRequestVersion++;
        var previous = livePlayerRequestSource;
        livePlayerRequestSource = null;
        previous?.Cancel();
    }
}
