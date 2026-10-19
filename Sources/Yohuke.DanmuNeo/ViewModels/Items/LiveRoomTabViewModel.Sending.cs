using System.Collections.Concurrent;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Models.State;
using Yohuke.DanmuNeo.Models.Workspace;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels.Items;

public partial class LiveRoomTabViewModel
{
    private readonly CancellationTokenSource sendLifetimeTokenSource = new();
    private CancellationTokenSource lyricSessionTokenSource = new();
    private readonly ConcurrentDictionary<LyricLineState, DanmuDeliveryProgress> lyricDeliveryProgress = new();
    private DanmuDeliveryProgress? draftDeliveryProgress;
    private bool isUpdatingDraftForSend;

    /// <summary>提交同传，继续相同失败草稿中尚未被接受的正文。</summary>
    [RelayCommand]
    public async Task SendDraftAsync()
    {
        if (isDisposed || string.IsNullOrWhiteSpace(InputDraft))
        {
            return;
        }
        var operationId = Interlocked.Increment(ref draftSendOperationId);
        IsDraftSendFailed = false;
        var draft = InputDraft;
        var revision = draftRevision;
        var settings = getSettings?.Invoke();
        var account = resolveAccount?.Invoke(this);
        if (settings is null || sendService is null)
        {
            log?.Error(CreateLogSource(Localization.Get("Room.Log.DraftSend")), Localization.Get("Room.Log.SendUnavailable"));
            TrySetDraftSendFeedback(operationId, revision, new("Room.Draft.ServiceUnavailable"), true);
            return;
        }
        var marks = getMarkSymbolGroup?.Invoke() ?? MarkSymbolService.CreateDefaultGroup(settings);
        var content = new DanmuMessageContent
        {
            Body = ShieldReplacementService.Apply(draft.Trim(), settings, false),
            Prefix = marks.TranslateOpenMark, Suffix = marks.TranslateCloseMark
        };
        var request = new DanmuSendRequest { RoomId = RoomId, Contents = [content] };
        var progress = GetProgress(request, account, draftDeliveryProgress);
        draftDeliveryProgress = progress;
        SetSendingDraft("");
        revision = draftRevision;
        SetDraftSendStatus(new("Room.Draft.Sending"));
        var source = CreateLogSource(Localization.Get("Room.Log.DraftSend"));
        DanmuDeliveryResult delivery;
        try
        {
            delivery = await SendMessageAsync(request, account, settings,
                source, progress);
        }
        catch (OperationCanceledException) when (sendLifetimeTokenSource.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            log?.Error(source, Localization.Get("Room.Log.SendFailed"), exception);
            TrySetDraftSendFeedback(operationId, revision, new("Room.Draft.Failed",
                new Func<string>(() => LocalizedExceptionText.Get(exception))), true, draft);
            return;
        }
        if (ReferenceEquals(draftDeliveryProgress, progress))
        {
            draftDeliveryProgress = delivery.IsComplete ? null : delivery.Progress;
        }
        var results = delivery.Results;
        var acceptedCount = results.Count(result => result.IsAccepted);
        var failure = results.FirstOrDefault(result => !result.IsAccepted);
        if (!delivery.IsComplete)
        {
            var error = failure is null ? new LocalizedMessage("Room.Draft.ServiceUnavailable") :
                acceptedCount == 0 ? new LocalizedMessage("Room.Draft.Failed", new Func<string>(() => failure.ErrorMessage)) :
                new LocalizedMessage("Room.Draft.PartiallyFailed", acceptedCount, results.Count, new Func<string>(() => failure.ErrorMessage));
            TrySetDraftSendFeedback(operationId, revision, error, true, draft);
            return;
        }
        TrySetDraftSendFeedback(operationId, revision, new("Room.Draft.Accepted"), false);
    }

    private async Task<bool> SendLyricLineAsync(LyricLineState line, bool forceSend,
        CancellationToken cancellationToken = default)
    {
        if (isDisposed || !lyricLinesSending.TryAdd(line, 0))
        {
            return false;
        }
        var source = CreateLogSource(Localization.Get("Room.Log.LyricSend"));
        using var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lyricSessionTokenSource.Token);
        try
        {
            if (!forceSend && PreventRepeatedLyricSend && line.IsSent)
            {
                return false;
            }
            var settings = getSettings?.Invoke();
            var account = resolveAccount?.Invoke(this);
            if (settings is null || sendService is null)
            {
                log?.Error(source, Localization.Get("Room.Log.SendUnavailable"));
                return false;
            }
            var marks = getMarkSymbolGroup?.Invoke() ?? MarkSymbolService.CreateDefaultGroup(settings);
            var contents = CreateLyricContents(line, settings, marks);
            if (contents.Count == 0)
            {
                return false;
            }
            var request = new DanmuSendRequest { RoomId = RoomId, Contents = contents };
            lyricDeliveryProgress.TryGetValue(line, out var previous);
            var progress = GetProgress(request, account, previous is not null && (!previous.IsComplete || !line.IsSent) ? previous : null);
            lyricDeliveryProgress[line] = progress;
            var result = await SendMessageAsync(request, account, settings, source, progress, sessionCancellation.Token);
            if (isDisposed || !Lyrics.Contains(line) || sessionCancellation.IsCancellationRequested)
            {
                return false;
            }
            lyricDeliveryProgress[line] = result.Progress;
            line.IsSent = result.IsComplete;
            return result.IsComplete;
        }
        catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested || sendLifetimeTokenSource.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            log?.Error(source, Localization.Get("Room.Log.LyricFailed"), exception);
            return false;
        }
        finally
        {
            lyricLinesSending.TryRemove(line, out _);
        }
    }

    private List<DanmuMessageContent> CreateLyricContents(LyricLineState line, AppSettings settings, MarkSymbolGroup marks)
    {
        var texts = LyricSendMode switch
        {
            LyricSendMode.OriginalOnly => new[] { ("original", line.Content) },
            LyricSendMode.TranslationOnly => new[] { string.IsNullOrWhiteSpace(line.TranslatedContent)
                ? ("original", line.Content) : ("translation", line.TranslatedContent) },
            _ => [("original", line.Content), ("translation", line.TranslatedContent)]
        };
        return texts.Where(text => !string.IsNullOrWhiteSpace(text.Item2))
            .DistinctBy(text => text.Item2.Trim())
            .Select(text => new DanmuMessageContent
            {
                Language = text.Item1, Body = ShieldReplacementService.Apply(text.Item2.Trim(), settings, true),
                Prefix = marks.LyricOpenMark, Suffix = marks.LyricCloseMark
            }).Where(content => !string.IsNullOrWhiteSpace(content.Body)).ToList();
    }

    private async Task<DanmuDeliveryResult> SendMessageAsync(DanmuSendRequest request, BilibiliAccount? account,
        AppSettings settings, string source, DanmuDeliveryProgress progress, CancellationToken cancellationToken = default)
    {
        if (sendService is null)
        {
            throw LocalizedExceptionText.Create("Room.Log.SendUnavailable", message => new InvalidOperationException(message));
        }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sendLifetimeTokenSource.Token);
        var sendingAccount = await PrepareDanmuSendingAccountAsync(request.RoomId, account, lifetime.Token);
        var result = await sendService.SendAsync(request, sendingAccount, settings, progress, lifetime.Token);
        lifetime.Token.ThrowIfCancellationRequested();
        var rejected = result.Results.Where(part => !part.IsAccepted).ToList();
        if (rejected.Count > 0)
        {
            var errors = rejected.Select(part => part.ApiCode is { } code
                ? Localization.Format("Room.Log.ApiError", part.ErrorMessage, code) : part.ErrorMessage).Distinct();
            var exceptions = rejected.Select(part => part.Error).OfType<Exception>().ToList();
            var details = exceptions.Count switch { 0 => null, 1 => exceptions[0], _ => new AggregateException(exceptions) };
            log?.Error(source, Localization.Format("Room.Log.SendPartiallyAccepted", result.Results.Count - rejected.Count,
                result.Results.Count, string.Join(Localization.Get("Room.Log.ErrorSeparator"), errors)), details);
        }
        return result;
    }

    private static DanmuDeliveryProgress GetProgress(DanmuSendRequest request, BilibiliAccount? account, DanmuDeliveryProgress? previous)
    {
        return previous is not null && previous.Matches(request, account) ? previous : new(request, account);
    }

    private void SetSendingDraft(string value)
    {
        isUpdatingDraftForSend = true;
        try
        {
            InputDraft = value;
        }
        finally
        {
            isUpdatingDraftForSend = false;
        }
    }

    private bool TrySetDraftSendFeedback(long operationId, long revision, LocalizedMessage status, bool failed,
        string? draftToRestore = null)
    {
        if (isDisposed || operationId != draftSendOperationId || revision != draftRevision)
        {
            return false;
        }

        if (draftToRestore is not null)
        {
            SetSendingDraft(draftToRestore);
        }

        SetDraftSendStatus(status);
        draftSendErrorMessage = failed ? status : new("");
        DraftSendError = draftSendErrorMessage.Render();
        IsDraftSendFailed = failed;
        return true;
    }

    private void InvalidateDraftSendFeedback()
    {
        draftRevision++;
        if (!isUpdatingDraftForSend)
        {
            draftDeliveryProgress = null;
        }
        IsDraftSendFailed = false;
    }

    private void OnSendRecordCreated(object? sender, DanmuFeedItem item)
    {
        if (isDisposed || item.UserName != RoomId)
        {
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            if (isDisposed || item.UserName != RoomId)
            {
                return;
            }
            var existing = string.IsNullOrEmpty(item.SendRecordId) ? null :
                TranslateHistoryItems.FirstOrDefault(record => record.SendRecordId == item.SendRecordId);
            if (existing is null)
            {
                TranslateHistoryItems.Insert(0, item);
                Trim(TranslateHistoryItems, 1000);
            }
            else
            {
                TranslateHistoryItems[TranslateHistoryItems.IndexOf(existing)] = item;
            }
            OnPropertyChanged(nameof(HasTranslateHistory));
            var failed = string.IsNullOrEmpty(item.SendRecordId) ? null :
                DanmuItems.FirstOrDefault(record => record.IsLocalRecord && record.SendRecordId == item.SendRecordId);
            if (failed is not null)
            {
                DanmuItems.Remove(failed);
            }
            if (!item.IsSendAccepted)
            {
                DanmuItems.Insert(0, item);
                TrimDanmuItems();
            }
        });
    }
}
