using Yohuke.DanmuNeo.I18n;
using System.Net.WebSockets;

namespace Yohuke.DanmuNeo.Services;

internal class LiveDanmuReconnectCoordinator
{
    private const double INITIAL_RECONNECT_DELAY_SECONDS = 2;
    private const double MAX_RECONNECT_DELAY_SECONDS = 30;

    private readonly Func<Action, CancellationToken, Task> connectOnce;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;

    internal LiveDanmuReconnectCoordinator(
        Func<Action, CancellationToken, Task> connectOnce,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.connectOnce = connectOnce;
        this.delay = delay ?? Task.Delay;
    }

    internal event EventHandler<Exception>? ErrorReceived;

    internal event EventHandler? Disconnected;

    internal event EventHandler? Recovered;

    internal async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var isOutage = false;
        var failureCount = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await connectOnce(() =>
                {
                    failureCount = 0;

                    if (!isOutage)
                    {
                        return;
                    }

                    isOutage = false;
                    Recovered?.Invoke(this, EventArgs.Empty);
                }, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                throw LocalizedExceptionText.Create("Services.Socket.ConnectionClosed",
                    message => new WebSocketException(message));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                ErrorReceived?.Invoke(this, exception);

                if (!isOutage)
                {
                    isOutage = true;
                    Disconnected?.Invoke(this, EventArgs.Empty);
                }

                var reconnectDelay = GetReconnectDelay(failureCount);
                failureCount++;

                try
                {
                    await delay(reconnectDelay, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    internal static TimeSpan GetReconnectDelay(int failureCount)
    {
        var exponent = Math.Clamp(failureCount, 0, 4);
        var seconds = INITIAL_RECONNECT_DELAY_SECONDS * Math.Pow(2, exponent);
        return TimeSpan.FromSeconds(Math.Min(seconds, MAX_RECONNECT_DELAY_SECONDS));
    }
}
