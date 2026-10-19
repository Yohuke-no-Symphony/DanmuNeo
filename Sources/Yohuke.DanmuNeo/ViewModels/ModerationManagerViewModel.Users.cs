using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Apis;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.ViewModels;

public sealed partial class ModerationManagerViewModel
{
    /// <summary>按精确 UID 或昵称查询用户，不要求其拥有直播间。</summary>
    [RelayCommand(CanExecute = nameof(CanSearchUsers), AllowConcurrentExecutions = true)]
    public Task SearchUsersAsync()
    {
        if (!CanSearchUsers())
        {
            return Task.CompletedTask;
        }

        CancelSearch();
        UserSearchResults.Clear();
        loadedSearchKeyword = SearchKeyword.Trim();
        loadedSearchPage = 0;
        HasMoreUsers = false;
        return SearchUserPageAsync(1);
    }

    /// <summary>加载当前昵称查询的下一页，并按 UID 去重。</summary>
    [RelayCommand(CanExecute = nameof(CanLoadMoreUsers))]
    public Task LoadMoreUsersAsync()
    {
        return CanLoadMoreUsers() ? SearchUserPageAsync(loadedSearchPage + 1) : Task.CompletedTask;
    }

    private bool CanSearchUsers()
    {
        return isActive && CaptureAccount(false) is not null && !IsWriting && !string.IsNullOrWhiteSpace(SearchKeyword);
    }

    private bool CanLoadMoreUsers()
    {
        return CanSearchUsers() && !IsSearching && HasMoreUsers && loadedSearchKeyword == SearchKeyword.Trim();
    }

    private async Task SearchUserPageAsync(int page)
    {
        var account = CaptureAccount(false);

        if (account is null)
        {
            return;
        }

        using var request = CreateRequest(account);
        var token = request.Token;
        searchCancellation = request;
        IsSearching = true;
        var keyword = loadedSearchKeyword;
        SetStatus("Moderation.UserSearching");

        try
        {
            var result = await moderationService.SearchUsersAsync(keyword, CanManage ? account.Cookie : null,
                page, getTimeout(), token);
            token.ThrowIfCancellationRequested();

            if (!IsCurrent(account) || keyword != loadedSearchKeyword || !ReferenceEquals(searchCancellation, request))
            {
                return;
            }

            foreach (var user in result.Users.Where(user => user.Uid > 0))
            {
                if (UserSearchResults.All(existing => existing.Uid != user.Uid))
                {
                    UserSearchResults.Add(user);
                }
            }

            loadedSearchPage = page;
            HasMoreUsers = result.HasMore;
            SetStatus(UserSearchResults.Count == 0 ? "Moderation.NoUsers" : "Moderation.UsersLoaded");
            NotifyCommands();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (BilibiliAuthenticationException)
        {
            if (IsCurrent(account))
            {
                accountSessions.ReportAuthenticationFailure(account.Id, account.Cookie);
                SetStatus("Moderation.CredentialsRequired");
            }
        }
        catch (Exception error)
        {
            if (IsCurrent(account) && !token.IsCancellationRequested)
            {
                SetStatus("Moderation.UserSearchFailed", new Func<string>(() => LocalizedExceptionText.Get(error)));
            }
        }
        finally
        {
            if (ReferenceEquals(searchCancellation, request))
            {
                searchCancellation = null;
                IsSearching = false;
            }
        }
    }
}
