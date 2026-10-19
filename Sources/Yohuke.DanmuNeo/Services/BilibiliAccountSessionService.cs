using System.ComponentModel;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;

namespace Yohuke.DanmuNeo.Services;

/// <summary>
/// 应用生命周期内共享的账号状态与凭据取消边界。
/// </summary>
public sealed class BilibiliAccountSessionService : IBilibiliAccountSessionService
{
    private readonly IBilibiliAccountValidationService validationService;
    private readonly Dictionary<string, AccountSessionEntry> entries = [];
    private readonly object sync = new();
    private bool isDisposed;

    /// <summary>
    /// 初始化账号会话，不主动联网。
    /// </summary>
    public BilibiliAccountSessionService(IBilibiliAccountValidationService? validationService = null)
    {
        this.validationService = validationService ?? new BilibiliAccountValidationService();
    }

    /// <inheritdoc/>
    public event EventHandler<string>? Changed;

    /// <inheritdoc/>
    public void Register(BilibiliAccount account)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            if (entries.ContainsKey(account.Id))
            {
                return;
            }

            entries.Add(account.Id, new()
            {
                Account = account, Cookie = account.Cookie,
                State = new()
                {
                    Status = string.IsNullOrWhiteSpace(account.Cookie)
                        ? AccountValidationStatus.NotBound : AccountValidationStatus.Unchecked
                }
            });
            account.PropertyChanged += OnAccountChanged;
        }
    }

    /// <inheritdoc/>
    public void Remove(string accountId)
    {
        lock (sync)
        {
            if (!entries.Remove(accountId, out var entry))
            {
                return;
            }

            entry.Account.PropertyChanged -= OnAccountChanged;
            entry.Credentials.Cancel();
            entry.Credentials.Dispose();
            entry.Validation?.Cancel();
        }

        Changed?.Invoke(this, accountId);
    }

    /// <inheritdoc/>
    public AccountValidationResult GetState(string accountId)
    {
        lock (sync)
        {
            return entries.TryGetValue(accountId, out var entry)
                ? entry.State : new() { Status = AccountValidationStatus.NotBound };
        }
    }

    /// <inheritdoc/>
    public bool CanSend(string accountId, string cookie)
    {
        lock (sync)
        {
            return !isDisposed && entries.TryGetValue(accountId, out var entry) && entry.Cookie == cookie &&
                   !entry.IsBlocked && BilibiliAccountValidationService.HasSendingCredentials(cookie);
        }
    }

    /// <inheritdoc/>
    public CancellationToken GetCredentialCancellationToken(string accountId, string cookie)
    {
        lock (sync)
        {
            return !isDisposed && entries.TryGetValue(accountId, out var entry) && entry.Cookie == cookie
                ? entry.Credentials.Token : new(true);
        }
    }

    /// <inheritdoc/>
    public async Task<AccountValidationResult> ValidateAsync(BilibiliAccount account, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        AccountSessionEntry entry;
        CancellationTokenSource request;
        long revision;
        string cookie;

        lock (sync)
        {
            if (isDisposed || !entries.TryGetValue(account.Id, out entry!))
            {
                return new() { Status = AccountValidationStatus.NotBound };
            }

            entry.Validation?.Cancel();
            request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, entry.Credentials.Token);
            entry.Validation = request;
            revision = ++entry.Revision;
            cookie = entry.Cookie;
            entry.State = entry.State with { Status = AccountValidationStatus.Checking };
        }

        Changed?.Invoke(this, account.Id);

        try
        {
            var result = await validationService.ValidateAsync(cookie, timeout, request.Token).ConfigureAwait(false);
            lock (sync)
            {
                if (isDisposed || !entries.TryGetValue(account.Id, out var current) ||
                    !ReferenceEquals(current, entry) || entry.Revision != revision || request.IsCancellationRequested)
                {
                    return GetState(account.Id);
                }

                entry.IsBlocked = result.Status switch
                {
                    AccountValidationStatus.Valid => false,
                    AccountValidationStatus.Expired or AccountValidationStatus.Incomplete or AccountValidationStatus.NotBound => true,
                    _ => entry.IsBlocked
                };
                entry.State = result with
                {
                    Uid = result.Uid > 0 ? result.Uid : entry.State.Uid,
                    UserName = result.Uid > 0 ? result.UserName : entry.State.UserName
                };
            }

            Changed?.Invoke(this, account.Id);
            return GetState(account.Id);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            return GetState(account.Id);
        }
        catch
        {
            lock (sync)
            {
                if (!isDisposed && entries.TryGetValue(account.Id, out var current) &&
                    ReferenceEquals(current, entry) && entry.Revision == revision)
                {
                    entry.State = entry.State with
                    {
                        Status = AccountValidationStatus.Unavailable, CheckedAt = DateTimeOffset.Now
                    };
                }
            }

            Changed?.Invoke(this, account.Id);
            return GetState(account.Id);
        }
        finally
        {
            lock (sync)
            {
                if (ReferenceEquals(entry.Validation, request))
                {
                    entry.Validation = null;
                }
            }

            request.Dispose();
        }
    }

    /// <inheritdoc/>
    public void ReportAuthenticationFailure(string accountId, string cookie)
    {
        lock (sync)
        {
            if (!entries.TryGetValue(accountId, out var entry) || entry.Cookie != cookie)
            {
                return;
            }

            entry.Revision++;
            entry.Validation?.Cancel();
            entry.IsBlocked = true;
            entry.State = entry.State with { Status = AccountValidationStatus.Expired, CheckedAt = DateTimeOffset.Now };
        }

        Changed?.Invoke(this, accountId);
    }

    private void OnAccountChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BilibiliAccount.Cookie) || sender is not BilibiliAccount account)
        {
            return;
        }

        lock (sync)
        {
            if (!entries.TryGetValue(account.Id, out var entry))
            {
                return;
            }

            entry.Revision++;
            entry.Validation?.Cancel();
            entry.Credentials.Cancel();
            entry.Credentials.Dispose();
            entry.Credentials = new();
            entry.Cookie = account.Cookie;
            entry.IsBlocked = false;
            entry.State = new()
            {
                Status = string.IsNullOrWhiteSpace(account.Cookie)
                    ? AccountValidationStatus.NotBound : AccountValidationStatus.Unchecked
            };
        }

        Changed?.Invoke(this, account.Id);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (sync)
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            foreach (var accountId in entries.Keys.ToArray())
            {
                Remove(accountId);
            }
        }
    }
}
