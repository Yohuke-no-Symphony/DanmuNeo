using System.ComponentModel;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>按账号串行协调自动与手动换牌，始终让后续手动意图在在途自动请求之后执行。</summary>
public sealed class MedalSwitchCoordinator : IDisposable, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly IBilibiliMedalService service;
    private readonly IBilibiliAccountSessionService sessions;
    private readonly Func<Task> saveState;
    private readonly Func<TimeSpan> getTimeout;
    private readonly IAppLogService log;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Dictionary<string, MedalAccountQueue> queues = [];
    private readonly HashSet<Task> operations = [];
    private MedalSwitchTarget? active;
    private long revision;
    private bool disposed;
    private bool resourcesDisposed;

    /// <summary>构造协调器，不主动查询牌墙或修改佩戴状态。</summary>
    public MedalSwitchCoordinator(IBilibiliMedalService service, IBilibiliAccountSessionService sessions,
        Func<Task> saveState, Func<TimeSpan> getTimeout, IAppLogService log)
    {
        this.service = service;
        this.sessions = sessions;
        this.saveState = saveState;
        this.getTimeout = getTimeout;
        this.log = log;
        sessions.Changed += OnSessionChanged;
    }

    /// <summary>牌墙刷新或写后确认完成，事件不包含凭据。</summary>
    public event EventHandler<AccountMedalSnapshot>? StateChanged;

    /// <summary>更新唯一活跃房间；自动关闭时不会发起任何房间或牌墙请求。</summary>
    public Task SetActiveRoomAsync(BilibiliAccount? account, string? roomId)
    {
        var normalized = long.TryParse(roomId, out var id) && id > 0 ? id.ToString() : "";
        return SetTarget(account, normalized, 0);
    }

    /// <summary>在调用方已确认真实主播UID时更新自动换牌目标。</summary>
    public Task SetActiveTargetAsync(BilibiliAccount? account, long ownerUid)
    {
        return SetTarget(account, "", Math.Max(0, ownerUid));
    }

    /// <summary>主动刷新账号牌墙；验证后的UID由统一账号会话提供。</summary>
    public Task<AccountMedalCollection> RefreshAsync(BilibiliAccount account, CancellationToken cancellationToken = default)
    {
        return Track(() => RefreshCoreAsync(account, cancellationToken));
    }

    /// <summary>先关闭并保存账号自动设置，再按账号写队列佩戴指定牌子。</summary>
    public Task<AccountMedalCollection> WearManuallyAsync(BilibiliAccount account, long medalId,
        CancellationToken cancellationToken = default)
    {
        return Track(() => WriteManuallyAsync(account, medalId, cancellationToken));
    }

    /// <summary>先关闭并保存账号自动设置，再摘下服务端确认的当前牌子。</summary>
    public Task<AccountMedalCollection> TakeOffManuallyAsync(BilibiliAccount account,
        CancellationToken cancellationToken = default)
    {
        return Track(() => WriteManuallyAsync(account, null, cancellationToken));
    }

    /// <summary>保存账号级自动偏好，启用后重新处理当前活跃目标。</summary>
    public Task SetAutomaticAsync(BilibiliAccount account, bool enabled)
    {
        return Track(async () =>
        {
            var intent = CaptureIntent(account);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,
                intent.CredentialCancellationToken);
            await SavePreferenceAsync(account, enabled, cancellation.Token).ConfigureAwait(false);
            if (enabled)
            {
                await QueueActive().ConfigureAwait(false);
            }
            return true;
        });
    }

    /// <summary>等待已排队的自动操作结束，用于关闭应用和离线验证。</summary>
    public async Task WhenIdleAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (gate)
            {
                pending = queues.Values.Where(queue => queue.Running).Select(queue => queue.Worker)
                    .Concat(operations).ToArray();
            }
            if (pending.Length == 0)
            {
                return;
            }
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (active is not null)
            {
                active.Account.PropertyChanged -= OnActiveAccountChanged;
            }
            active = null;
            sessions.Changed -= OnSessionChanged;
            lifetime.Cancel();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        Dispose();
        Task[] pending;
        lock (gate)
        {
            pending = queues.Values.Select(queue => queue.Worker).Concat(operations).ToArray();
        }
        try
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            log.Error(Localization.Get("Medals.Source"), Localization.Get("Medals.ShutdownFailed"), exception);
        }
        lock (gate)
        {
            if (resourcesDisposed)
            {
                return;
            }
            resourcesDisposed = true;
            foreach (var queue in queues.Values)
            {
                queue.Serial.Dispose();
                queue.PreferenceSerial.Dispose();
            }
            lifetime.Dispose();
        }
    }

    private Task SetTarget(BilibiliAccount? account, string roomId, long ownerUid)
    {
        lock (gate)
        {
            if (disposed)
            {
                return Task.CompletedTask;
            }
            if (active is not null && ReferenceEquals(active.Account, account) && active.Cookie == account?.Cookie &&
                active.RoomId == roomId && active.OwnerUid == ownerUid)
            {
                return QueueActive();
            }
            if (active is not null)
            {
                active.Account.PropertyChanged -= OnActiveAccountChanged;
            }
            active = account is null ? null : new(account, account.Cookie, roomId, ownerUid, ++revision);
            if (active is not null)
            {
                active.Account.PropertyChanged += OnActiveAccountChanged;
            }
            return QueueActive();
        }
    }

    private Task QueueActive()
    {
        lock (gate)
        {
            if (disposed || active is not { } target || !target.Account.AutoSwitchMedal ||
                target.OwnerUid <= 0 && target.RoomId.Length == 0 || target.Account.Cookie != target.Cookie)
            {
                return Task.CompletedTask;
            }
            var state = sessions.GetState(target.Account.Id);
            if (state.Status != AccountValidationStatus.Valid || state.Uid <= 0)
            {
                return Task.CompletedTask;
            }
            var queue = GetQueue(target.Account.Id);
            if (queue.PreferenceSaving)
            {
                return Task.CompletedTask;
            }
            if (queue.LastQueuedRevision == target.Revision && queue.LastQueuedUid == state.Uid)
            {
                return queue.Worker;
            }
            queue.LastQueuedRevision = target.Revision;
            queue.LastQueuedUid = state.Uid;
            queue.Pending = target;
            if (!queue.Running)
            {
                queue.Running = true;
                queue.Worker = Task.Run(() => RunAutomaticAsync(queue));
            }
            return queue.Worker;
        }
    }

    private async Task RunAutomaticAsync(MedalAccountQueue queue)
    {
        while (true)
        {
            MedalSwitchTarget? target;
            lock (gate)
            {
                target = queue.Pending;
                queue.Pending = null;
                if (disposed || target is null)
                {
                    queue.Running = false;
                    return;
                }
            }
            try
            {
                await queue.Serial.WaitAsync(lifetime.Token).ConfigureAwait(false);
                try
                {
                    if (!IsCurrentAutomatic(target))
                    {
                        continue;
                    }
                    var context = await CreateContextAsync(CaptureIntent(target.Account, target.Cookie), lifetime.Token, false).ConfigureAwait(false);
                    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,
                        context.CredentialCancellationToken);
                    var ownerUid = target.OwnerUid > 0 ? target.OwnerUid :
                        await service.GetRoomOwnerUidAsync(target.RoomId, timeout: getTimeout(),
                            cancellationToken: cancellation.Token).ConfigureAwait(false);
                    if (ownerUid <= 0 || !IsCurrentAutomatic(target))
                    {
                        continue;
                    }
                    var collection = await ReadAsync(context, cancellation.Token).ConfigureAwait(false);
                    if (!IsCurrentAutomatic(target))
                    {
                        continue;
                    }
                    var match = collection.Items.FirstOrDefault(item => item.TargetUid == ownerUid);
                    var wearing = collection.Items.FirstOrDefault(item => item.IsWearing);
                    if (match is not null && !match.IsWearing)
                    {
                        EnsureCurrentCredentials(context);
                        await service.WearAsync(match.Id, context.Cookie, getTimeout(), cancellation.Token).ConfigureAwait(false);
                        collection = await ReadAsync(context, cancellation.Token).ConfigureAwait(false);
                    }
                    else if (match is null && collection.IsComplete && wearing is not null)
                    {
                        EnsureCurrentCredentials(context);
                        await service.TakeOffAsync(wearing.Id, context.Cookie, getTimeout(), cancellation.Token).ConfigureAwait(false);
                        collection = await ReadAsync(context, cancellation.Token).ConfigureAwait(false);
                    }
                    else if (match is null && !collection.IsComplete)
                    {
                        log.Warning(Localization.Get("Medals.Source"), Localization.Get("Medals.Incomplete"));
                    }
                    Publish(context, collection);
                }
                finally
                {
                    queue.Serial.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (BilibiliAuthenticationException exception)
            {
                sessions.ReportAuthenticationFailure(target.Account.Id, target.Cookie);
                log.Error(Localization.Get("Medals.Source"), Localization.Get("Medals.AutomaticFailed"), exception);
            }
            catch (Exception exception)
            {
                log.Error(Localization.Get("Medals.Source"), Localization.Get("Medals.AutomaticFailed"), exception);
            }
        }
    }

    private async Task<AccountMedalCollection> RefreshCoreAsync(BilibiliAccount account, CancellationToken cancellationToken)
    {
        var intent = CaptureIntent(account);
        var queue = GetQueue(account.Id);
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken,
            intent.CredentialCancellationToken);
        await queue.Serial.WaitAsync(waiting.Token).ConfigureAwait(false);
        try
        {
            var context = await CreateContextAsync(intent, waiting.Token, true).ConfigureAwait(false);
            using var request = CancellationTokenSource.CreateLinkedTokenSource(waiting.Token, context.CredentialCancellationToken);
            var collection = await ReadAsync(context, request.Token).ConfigureAwait(false);
            Publish(context, collection);
            return collection;
        }
        finally
        {
            queue.Serial.Release();
        }
    }

    private async Task<AccountMedalCollection> WriteManuallyAsync(BilibiliAccount account, long? medalId,
        CancellationToken cancellationToken)
    {
        var intent = CaptureIntent(account);
        var queue = GetQueue(account.Id);
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken,
            intent.CredentialCancellationToken);
        await SavePreferenceAsync(account, false, waiting.Token).ConfigureAwait(false);
        await queue.Serial.WaitAsync(waiting.Token).ConfigureAwait(false);
        try
        {
            var context = await CreateContextAsync(intent, waiting.Token, true).ConfigureAwait(false);
            using var writeLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,
                context.CredentialCancellationToken);
            var collection = await ReadAsync(context, writeLifetime.Token).ConfigureAwait(false);
            var selected = medalId is { } id
                ? collection.Items.FirstOrDefault(item => item.Id == id)
                : collection.Items.FirstOrDefault(item => item.IsWearing);
            if (selected is null)
            {
                if (medalId is null && collection.IsComplete)
                {
                    Publish(context, collection);
                    return collection;
                }
                throw new InvalidOperationException(Localization.Get("Medals.SelectionMissing"));
            }
            EnsureCurrentCredentials(context);
            try
            {
                if (medalId.HasValue)
                {
                    await service.WearAsync(selected.Id, context.Cookie, getTimeout(), writeLifetime.Token).ConfigureAwait(false);
                }
                else
                {
                    await service.TakeOffAsync(selected.Id, context.Cookie, getTimeout(), writeLifetime.Token).ConfigureAwait(false);
                }
            }
            catch (BilibiliAuthenticationException)
            {
                sessions.ReportAuthenticationFailure(account.Id, context.Cookie);
                throw;
            }
            collection = await ReadAsync(context, writeLifetime.Token).ConfigureAwait(false);
            Publish(context, collection);
            return collection;
        }
        finally
        {
            queue.Serial.Release();
        }
    }

    private async Task SavePreferenceAsync(BilibiliAccount account, bool enabled, CancellationToken cancellationToken)
    {
        var queue = GetQueue(account.Id);
        await queue.PreferenceSerial.WaitAsync(cancellationToken);
        var previous = account.AutoSwitchMedal;
        lock (gate)
        {
            queue.PreferenceSaving = true;
        }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            account.AutoSwitchMedal = enabled;
            await saveState().WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            if (enabled)
            {
                account.AutoSwitchMedal = previous;
            }
            throw;
        }
        finally
        {
            lock (gate)
            {
                queue.PreferenceSaving = false;
            }
            queue.PreferenceSerial.Release();
        }
    }

    private MedalAccountOperationContext CaptureIntent(BilibiliAccount account, string? expectedCookie = null)
    {
        var cookie = expectedCookie ?? account.Cookie;
        return new(account, cookie, 0, sessions.GetCredentialCancellationToken(account.Id, cookie));
    }

    private async Task<MedalAccountOperationContext> CreateContextAsync(MedalAccountOperationContext intent,
        CancellationToken cancellationToken, bool validate)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token,
            intent.CredentialCancellationToken, cancellationToken);
        request.Token.ThrowIfCancellationRequested();
        var state = sessions.GetState(intent.Account.Id);
        if (validate && (state.Status != AccountValidationStatus.Valid || state.Uid <= 0))
        {
            state = await sessions.ValidateAsync(intent.Account, getTimeout(), request.Token).ConfigureAwait(false);
        }
        request.Token.ThrowIfCancellationRequested();
        if (state.Status != AccountValidationStatus.Valid || state.Uid <= 0 || intent.Account.Cookie != intent.Cookie ||
            !sessions.CanSend(intent.Account.Id, intent.Cookie))
        {
            throw new InvalidOperationException(Localization.Get("Medals.AccountInvalid"));
        }
        return intent with { Uid = state.Uid };
    }

    private async Task<AccountMedalCollection> ReadAsync(MedalAccountOperationContext context, CancellationToken cancellationToken)
    {
        try
        {
            var collection = await service.GetMedalsAsync(context.Uid, context.Cookie, getTimeout(), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureCurrentCredentials(context);
            return collection;
        }
        catch (BilibiliAuthenticationException)
        {
            sessions.ReportAuthenticationFailure(context.Account.Id, context.Cookie);
            throw;
        }
    }

    private void EnsureCurrentCredentials(MedalAccountOperationContext context)
    {
        context.CredentialCancellationToken.ThrowIfCancellationRequested();
        lifetime.Token.ThrowIfCancellationRequested();
        if (context.Account.Cookie != context.Cookie || !sessions.CanSend(context.Account.Id, context.Cookie))
        {
            throw new OperationCanceledException();
        }
    }

    private void Publish(MedalAccountOperationContext context, AccountMedalCollection collection)
    {
        EnsureCurrentCredentials(context);
        StateChanged?.Invoke(this, new(context.Account.Id, collection, context.CredentialCancellationToken));
    }

    private bool IsCurrentAutomatic(MedalSwitchTarget target)
    {
        lock (gate)
        {
            return !disposed && !GetQueue(target.Account.Id).PreferenceSaving && active?.Revision == target.Revision && target.Account.AutoSwitchMedal &&
                target.Account.Cookie == target.Cookie && sessions.GetState(target.Account.Id).Status == AccountValidationStatus.Valid;
        }
    }

    private MedalAccountQueue GetQueue(string accountId)
    {
        lock (gate)
        {
            if (!queues.TryGetValue(accountId, out var queue))
            {
                queue = new();
                queues.Add(accountId, queue);
            }
            return queue;
        }
    }

    private Task<T> Track<T>(Func<Task<T>> operation)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var task = operation();
            operations.Add(task);
            _ = task.ContinueWith(_ =>
            {
                lock (gate)
                {
                    operations.Remove(task);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }

    private void OnActiveAccountChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(BilibiliAccount.AutoSwitchMedal) or nameof(BilibiliAccount.Cookie)))
        {
            return;
        }
        lock (gate)
        {
            if (active is null || disposed)
            {
                return;
            }
            active = active with { Cookie = active.Account.Cookie, Revision = ++revision };
        }
        _ = QueueActive();
    }

    private void OnSessionChanged(object? sender, string accountId)
    {
        lock (gate)
        {
            if (disposed || active?.Account.Id != accountId)
            {
                return;
            }

            if (sessions.GetState(accountId).Status != AccountValidationStatus.Valid)
            {
                var queue = GetQueue(accountId);
                queue.LastQueuedRevision = -1;
                queue.LastQueuedUid = 0;
            }
        }
        _ = QueueActive();
    }
}
