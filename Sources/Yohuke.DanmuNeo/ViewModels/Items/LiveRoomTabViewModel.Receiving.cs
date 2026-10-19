using System.Globalization;
using Avalonia.Media;
using Yohuke.DanmuNeo.Models.Workspace;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.I18n;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.ViewModels.Items;

public partial class LiveRoomTabViewModel
{
    private const int MAX_RECENT_SUPER_CHAT_IDENTITIES = 512;
    private const int MAX_RECENT_GIFT_COMBOS = 300;
    private const string DEFAULT_SUPER_CHAT_COLOR = "#2A60B2";
    private CancellationTokenSource? listenTokenSource;
    private ILiveDanmuSocket? activeListenSocket;
    private readonly HashSet<string> recentSuperChatIdentities = new(StringComparer.Ordinal);
    private readonly Queue<string> recentSuperChatIdentityOrder = new();
    private readonly HashSet<string> recentGuardPurchaseIdentities = new(StringComparer.Ordinal);
    private readonly Queue<string> recentGuardPurchaseIdentityOrder = new();
    private readonly Dictionary<string, (DanmuFeedItem Item, int Count, decimal UnitPrice, LinkedListNode<string> OrderNode)> giftComboItems = new(StringComparer.Ordinal);
    private readonly Dictionary<DanmuFeedItem, string> giftComboKeysByItem = new();
    private readonly LinkedList<string> giftComboOrder = new();
    private string? listenerLogSource;

    private readonly DanmuHistoryReconciler historyReconciler = new();
    private LiveRoomReceptionServices? receptionServices;
    private string receptionWorkspaceId = "";
    private long listeningGeneration;
    private LiveRoomReceiveContext? activeReception;

    private void RefreshListeningAccountContext()
    {
        if (!isDisposed && IsListening && activeReception is { } context && !IsCurrentReception(context))
        {
            StopListening();
            StartListening();
        }
    }

    internal Task PendingHistoryTask { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// 开始监听直播间。
    /// </summary>
    [RelayCommand]
    public void StartListening()
    {
        if (isDisposed || IsListening)
        {
            return;
        }

        var account = resolveAccount?.Invoke(this);

        if (account is null || string.IsNullOrWhiteSpace(account.Cookie) || string.IsNullOrWhiteSpace(RoomId))
        {
            SetConnectionStatus("Room.MissingAccountOrRoom");
            log?.Error(CreateLogSource(Localization.Get("Room.Log.Listening")), Localization.Get("Room.Log.ListenMissingAccount"));
            return;
        }

        if (!long.TryParse(RoomId, out var parsedRoomId) || parsedRoomId <= 0)
        {
            SetConnectionStatus("Room.InvalidRoomId");
            log?.Error(CreateLogSource(Localization.Get("Room.Log.Listening")), Localization.Get("Room.Log.ListenInvalidRoom"));
            return;
        }

        var source = CreateLogSource(Localization.Get("Room.Log.Listening"));
        log?.SetDiagnosticError(source, null);
        ILiveDanmuSocket socket;

        try
        {
            socket = liveDanmuSocketFactory.Create(parsedRoomId, account.Cookie);
        }
        catch (Exception exception)
        {
            log?.Error(source, Localization.Get("Room.Log.ListenFailed"), exception);
            SetConnectionStatus("Room.ListenFailed", new Func<string>(() => LocalizedExceptionText.Get(exception)));
            return;
        }

        var tokenSource = new CancellationTokenSource();
        var cancellationToken = tokenSource.Token;
        var context = new LiveRoomReceiveContext(socket, RoomId, account.Id, account.Cookie,
            ++listeningGeneration, cancellationToken);
        activeReception = context;
        RefreshArchiveSession(context);
        ClearGiftComboState();
        listenTokenSource = tokenSource;
        activeListenSocket = socket;
        listenerLogSource = source;
        socket.DanmuReceived += OnDanmuReceived;
        socket.GiftReceived += OnGiftReceived;
        socket.SuperChatReceived += OnSuperChatReceived;
        socket.GuardPurchaseReceived += OnGuardPurchaseReceived;
        EventHandler onDisconnected = (_, _) => PostListenerConnectionStatus(socket, "Room.Reconnecting");
        EventHandler onRecovered = (_, _) =>
        {
            if (ReferenceEquals(socket, activeListenSocket) && !cancellationToken.IsCancellationRequested)
            {
                log?.SetDiagnosticError(source, null);
                PostListenerConnectionStatus(socket, "Room.Listening");
                RequestRecentHistory(context);
            }
        };
        EventHandler<Exception> onError = (_, exception) =>
        {
            if (ReferenceEquals(socket, activeListenSocket) && !cancellationToken.IsCancellationRequested)
            {
                log?.SetDiagnosticError(source, exception);
                PostListenerConnectionStatus(socket, "Room.Reconnecting");
            }
        };
        socket.Disconnected += onDisconnected;
        socket.Recovered += onRecovered;
        socket.ErrorReceived += onError;
        IsListening = true;
        SetConnectionStatus("Room.Listening");
        RequestRecentHistory(context);

        _ = Task.Run((Func<Task?>)(async () =>
        {
            try
            {
                await socket.StartAsync(tokenSource.Token);
            }
            catch (OperationCanceledException) when (tokenSource.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    log?.SetDiagnosticError(source, exception);
                }
            }
            finally
            {
                socket.DanmuReceived -= OnDanmuReceived;
                socket.GiftReceived -= OnGiftReceived;
                socket.SuperChatReceived -= OnSuperChatReceived;
                socket.GuardPurchaseReceived -= OnGuardPurchaseReceived;
                socket.Disconnected -= onDisconnected;
                socket.Recovered -= onRecovered;
                socket.ErrorReceived -= onError;
                if (socket is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (ReferenceEquals(listenTokenSource, tokenSource))
                    {
                        listenTokenSource = null;
                        activeListenSocket = null;
                        CloseReception(context);
                        listenerLogSource = null;
                        IsListening = false;
                        SetConnectionStatus("Room.Disconnected");
                        ClearGiftComboState();
                    }

                    tokenSource.Dispose();
                });
            }
        }));
    }

    /// <summary>
    /// 停止监听直播间。
    /// </summary>
    [RelayCommand]
    public void StopListening()
    {
        InvalidateModerationPermission();
        activeListenSocket = null;
        if (activeReception is { } context)
        {
            CloseReception(context);
        }

        if (listenerLogSource is { } source)
        {
            log?.SetDiagnosticError(source, null);
            listenerLogSource = null;
        }
        ClearGiftComboState();
        var tokenSource = listenTokenSource;
        listenTokenSource = null;
        tokenSource?.Cancel();
        IsListening = false;
        SetConnectionStatus("Room.Disconnected");
    }

    private void PostListenerConnectionStatus(ILiveDanmuSocket socket, string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(socket, activeListenSocket))
            {
                SetConnectionStatus(status);
            }
        });
    }

    /// <summary>应用归档开关，使当前监听会话立即开始或结束留档。</summary>
    public void RefreshDanmuArchiving()
    {
        if (activeReception is { } context)
        {
            RefreshArchiveSession(context);
        }
    }

    private bool IsCurrentReception(LiveRoomReceiveContext context)
    {
        var account = resolveAccount?.Invoke(this);
        return !isDisposed && account is not null && ReferenceEquals(activeReception, context) &&
               ReferenceEquals(activeListenSocket, context.Socket) && !context.CancellationToken.IsCancellationRequested &&
               context.Generation == listeningGeneration && context.RoomId == RoomId &&
               context.AccountId == account.Id && context.Cookie == account.Cookie;
    }

    private void RequestRecentHistory(LiveRoomReceiveContext context)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => RequestRecentHistory(context));
            return;
        }

        if (receptionServices is null || !IsCurrentReception(context))
        {
            return;
        }

        if (!context.HistoryTask.IsCompleted)
        {
            context.HistoryRefreshRequested = true;
            return;
        }

        context.HistoryTask = LoadRecentHistoryAsync(context);
        PendingHistoryTask = context.HistoryTask;
    }

    private async Task LoadRecentHistoryAsync(LiveRoomReceiveContext context)
    {
        try
        {
            var seconds = getSettings?.Invoke().TimeoutSeconds ?? 3.05;
            var timeout = TimeSpan.FromSeconds(double.IsFinite(seconds) ? Math.Clamp(seconds, 0.1, 120) : 3.05);
            var messages = await receptionServices!.History.GetRecentAsync(context.RoomId, context.Cookie,
                timeout, context.CancellationToken).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!IsCurrentReception(context))
                {
                    return;
                }

                foreach (var message in historyReconciler.AcceptHistory(messages.Where(item => item.RoomId == context.RoomId)))
                {
                    ArchiveDanmu(context, message, true);
                    if (!ShouldHideDanmu(message))
                    {
                        AddDanmuToFeed(message, true);
                    }
                }
            });
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (IsCurrentReception(context))
                {
                    log?.Warning(CreateLogSource(Localization.Get("Room.History.Source")),
                        Localization.Get("Room.History.Failed"), exception.Message);
                }
            });
        }
        finally
        {
            if (context.HistoryRefreshRequested)
            {
                context.HistoryRefreshRequested = false;
                Dispatcher.UIThread.Post(() =>
                {
                    context.HistoryTask = Task.CompletedTask;
                    RequestRecentHistory(context);
                });
            }
        }
    }

    private void ArchiveDanmu(LiveRoomReceiveContext context, BilibiliDanmuMessage message, bool history, DateTimeOffset? receivedAt = null)
    {
        ArchiveReceived(context, new("弹幕", message.Uid.ToString(System.Globalization.CultureInfo.InvariantCulture),
            message.UserName, message.Content, receivedAt ?? DateTimeOffset.Now, message.Timestamp, history));
    }

    private void ArchiveReceived(LiveRoomReceiveContext context, ReceivedDanmuLogEntry entry)
    {
        if (getSettings?.Invoke().ArchiveReceivedDanmu == true && context.ArchiveSession is { } session)
        {
            receptionServices?.Archive?.TryAppend(session, entry);
        }
    }

    private void RefreshArchiveSession(LiveRoomReceiveContext context)
    {
        if (getSettings?.Invoke().ArchiveReceivedDanmu == true && context.ArchiveSession is null)
        {
            context.ArchiveSession = receptionServices?.Archive?.OpenSession(context.RoomId, receptionWorkspaceId, Id);
        }
        else if (getSettings?.Invoke().ArchiveReceivedDanmu != true && context.ArchiveSession is { } session)
        {
            context.ArchiveSession = null;
            _ = receptionServices?.Archive?.CompleteSessionAsync(session);
        }
    }

    private void CloseReception(LiveRoomReceiveContext context)
    {
        if (ReferenceEquals(activeReception, context))
        {
            activeReception = null;
        }

        if (context.ArchiveSession is { } session)
        {
            context.ArchiveSession = null;
            _ = receptionServices?.Archive?.CompleteSessionAsync(session);
        }
    }
    private void OnDanmuReceived(object? sender, BilibiliDanmuMessage message)
    {
        var context = activeReception;
        var receivedAt = DateTimeOffset.Now;
        Dispatcher.UIThread.Post(() =>
        {
            if (context is not null && ReferenceEquals(sender, context.Socket) && IsCurrentReception(context) &&
                historyReconciler.AcceptLive(message))
            {
                ArchiveDanmu(context, message, false, receivedAt);
                if (!ShouldHideDanmu(message))
                {
                    AddDanmuToFeed(message);
                }
            }
        });
    }

    internal void AddDanmu(BilibiliDanmuMessage message)
    {
        if (historyReconciler.AcceptLive(message) && !ShouldHideDanmu(message))
        {
            AddDanmuToFeed(message);
        }
    }

    private bool ShouldHideDanmu(BilibiliDanmuMessage message)
    {
        return getSettings?.Invoke().HideEmoticonDanmu == true && message.IsEmoticon;
    }

    private void AddDanmuToFeed(BilibiliDanmuMessage message, bool isHistory = false)
    {
        var item = new DanmuFeedItem
        {
            RoomId = message.RoomId,
            IsRoomAdmin = message.IsRoomAdmin,
            IsHistory = isHistory,
            Time = message.Timestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(message.Timestamp) : DateTimeOffset.Now,
            UserName = message.UserName,
            UserUid = message.Uid.ToString(),
            MedalName = message.MedalName,
            MedalLevel = message.MedalLevel,
            MedalGuardLevel = message.MedalGuardLevel,
            Content = message.Content
        };
        var index = 0;
        if (isHistory && message.Timestamp <= 0)
        {
            index = DanmuItems.Count;
        }
        else if (isHistory)
        {
            while (index < DanmuItems.Count && DanmuItems[index].Time >= item.Time)
            {
                index++;
            }
        }

        DanmuItems.Insert(index, item);
        TrimDanmuItems();
        if (emoticonCacheService is not null && message.Emoticons.Count > 0)
        {
            _ = LoadDanmuEmoticonsAsync(item, message.Emoticons, emoticonCacheService,
                listenTokenSource?.Token ?? CancellationToken.None);
        }
    }

    private async Task LoadDanmuEmoticonsAsync(
        DanmuFeedItem item,
        IReadOnlyList<BilibiliDanmuEmoticon> emoticons,
        IEmoticonCacheService cacheService,
        CancellationToken cancellationToken)
    {
        var source = CreateLogSource(Localization.Get("Room.Log.Emoticon"), item.RoomId);

        try
        {
            var downloads = emoticons.Select(async emoticon => new DanmuEmoticon
            {
                Text = emoticon.Text,
                ImagePath = await cacheService.GetEmoticonAsync(emoticon.Url, cancellationToken).ConfigureAwait(false)
            });
            var cachedEmoticons = await Task.WhenAll(downloads).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!cancellationToken.IsCancellationRequested && DanmuItems.Contains(item))
                {
                    item.Emoticons = cachedEmoticons.Where(emoticon => !string.IsNullOrWhiteSpace(emoticon.ImagePath))
                        .ToArray();
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (log is null)
            {
                System.Diagnostics.Trace.TraceWarning(Localization.Format("Room.Log.EmoticonException", exception.GetType().Name));
                return;
            }

            log.Error(source, Localization.Get("Room.Log.EmoticonFailed"), exception);
        }
    }

    private void OnGiftReceived(object? sender, BilibiliGiftMessage message)
    {
        var context = activeReception;
        var receivedAt = DateTimeOffset.Now;
        Dispatcher.UIThread.Post(() =>
        {
            if (context is not null && ReferenceEquals(sender, context.Socket) && IsCurrentReception(context))
            {
                ArchiveReceived(context, new("礼物", message.Uid.ToString(CultureInfo.InvariantCulture), message.UserName,
                    string.Create(CultureInfo.InvariantCulture, $"{message.GiftName} ×{message.Quantity} (单价 {message.UnitPrice / 1000m:0.###} 元)"),
                    receivedAt, message.Timestamp));
                AddGiftToFeed(message);
            }
        });
    }

    internal void AddGift(BilibiliGiftMessage message)
    {
        AddGiftToFeed(message);
    }

    private void AddGiftToFeed(BilibiliGiftMessage message)
    {
        if (message.Uid <= 0 || string.IsNullOrWhiteSpace(message.UserName) ||
            string.IsNullOrWhiteSpace(message.GiftName) || message.Quantity <= 0)
        {
            return;
        }

        var comboIdentity = CreateGiftComboIdentity(message);

        if (!string.IsNullOrWhiteSpace(comboIdentity) && giftComboItems.TryGetValue(comboIdentity, out var combo))
        {
            if (DanmuItems.Contains(combo.Item))
            {
                var count = message.BatchComboCount > 0
                    ? Math.Max(combo.Count, message.BatchComboCount)
                    : (int)Math.Min(int.MaxValue, (long)combo.Count + message.Quantity);
                var unitPrice = message.UnitPrice > 0 ? message.UnitPrice : combo.UnitPrice;
                SetGiftContent(combo.Item, message.GiftName, count, unitPrice);
                giftComboItems[comboIdentity] = (combo.Item, count, unitPrice, combo.OrderNode);
                return;
            }

            RemoveGiftCombo(combo.Item);
        }

        var initialCount = Math.Max(message.Quantity, message.BatchComboCount);
        var item = new DanmuFeedItem
        {
            RoomId = message.RoomId,
            Time = message.Timestamp > 0
                ? DateTimeOffset.FromUnixTimeSeconds(message.Timestamp)
                : DateTimeOffset.Now,
            UserName = message.UserName,
            UserUid = message.Uid.ToString(CultureInfo.InvariantCulture)
        };
        SetGiftContent(item, message.GiftName, initialCount, message.UnitPrice);

        DanmuItems.Insert(0, item);
        TrimDanmuItems();

        if (!string.IsNullOrWhiteSpace(comboIdentity))
        {
            AddGiftCombo(comboIdentity, item, initialCount, message.UnitPrice);
        }
    }

    private static string CreateGiftComboIdentity(BilibiliGiftMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.BatchComboId))
        {
            return "";
        }

        var giftIdentity = string.IsNullOrWhiteSpace(message.GiftId) ? message.GiftName : message.GiftId;
        return string.Join(
            '|',
            message.RoomId,
            message.Uid.ToString(CultureInfo.InvariantCulture),
            giftIdentity,
            message.BatchComboId);
    }

    private static void SetGiftContent(DanmuFeedItem item, string giftName, int count, decimal unitPrice)
    {
        var price = "";
        var totalPrice = unitPrice * count / 1000m;

        if (totalPrice > 0)
        {
            price = $" · ￥{totalPrice.ToString("0.##", CultureInfo.InvariantCulture)}";
        }

        item.SetLocalizedContent("Room.GiftContent", giftName, count, price);
    }

    private void AddGiftCombo(string identity, DanmuFeedItem item, int count, decimal unitPrice)
    {
        while (giftComboItems.Count >= MAX_RECENT_GIFT_COMBOS && giftComboOrder.First is { } oldest)
        {
            if (giftComboItems.TryGetValue(oldest.Value, out var oldestCombo))
            {
                RemoveGiftCombo(oldestCombo.Item);
            }
            else
            {
                giftComboOrder.RemoveFirst();
            }
        }

        var orderNode = giftComboOrder.AddLast(identity);
        giftComboItems[identity] = (item, count, unitPrice, orderNode);
        giftComboKeysByItem[item] = identity;
    }

    private void RemoveGiftCombo(DanmuFeedItem item)
    {
        if (!giftComboKeysByItem.Remove(item, out var identity) || !giftComboItems.Remove(identity, out var combo))
        {
            return;
        }

        giftComboOrder.Remove(combo.OrderNode);
    }

    private void ClearGiftComboState()
    {
        giftComboItems.Clear();
        giftComboKeysByItem.Clear();
        giftComboOrder.Clear();
    }

    private void TrimDanmuItems()
    {
        while (DanmuItems.Count > 300)
        {
            var removedItem = DanmuItems[^1];
            DanmuItems.RemoveAt(DanmuItems.Count - 1);
            RemoveGiftCombo(removedItem);
        }
    }

    private void OnSuperChatReceived(object? sender, BilibiliSuperChatMessage message)
    {
        var context = activeReception;
        var receivedAt = DateTimeOffset.Now;
        Dispatcher.UIThread.Post(() =>
        {
            if (context is not null && ReferenceEquals(sender, context.Socket) && IsCurrentReception(context))
            {
                ArchiveReceived(context, new("SC", message.Uid.ToString(CultureInfo.InvariantCulture), message.UserName,
                    $"{message.PriceText} {message.Content}", receivedAt, message.Timestamp));
                AddSuperChat(message);
            }
        });
    }

    internal void AddSuperChat(BilibiliSuperChatMessage message)
    {
        var identity = CreateSuperChatIdentity(message);

        if (!recentSuperChatIdentities.Add(identity))
        {
            return;
        }

        recentSuperChatIdentityOrder.Enqueue(identity);

        while (recentSuperChatIdentityOrder.Count > MAX_RECENT_SUPER_CHAT_IDENTITIES)
        {
            recentSuperChatIdentities.Remove(recentSuperChatIdentityOrder.Dequeue());
        }

        SuperChats.Insert(0, new()
        {
            MessageId = message.MessageId,
            Time = DateTimeOffset.FromUnixTimeSeconds(message.Timestamp),
            UserName = message.UserName,
            PriceText = message.PriceText,
            BorderColor = CreateSuperChatBorderColor(message.BorderColor),
            Content = message.Content
        });
        Trim(SuperChats, 100);
    }

    private static string CreateSuperChatIdentity(BilibiliSuperChatMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.MessageId))
        {
            return $"id:{message.MessageId.Trim()}";
        }

        return string.Join(
            '|',
            "fallback",
            message.RoomId,
            message.Timestamp.ToString(CultureInfo.InvariantCulture),
            message.UserName,
            message.Price.ToString(CultureInfo.InvariantCulture),
            message.Content);
    }

    private void OnGuardPurchaseReceived(object? sender, BilibiliGuardPurchaseMessage message)
    {
        var context = activeReception;
        var receivedAt = DateTimeOffset.Now;
        Dispatcher.UIThread.Post(() =>
        {
            if (context is not null && ReferenceEquals(sender, context.Socket) && IsCurrentReception(context))
            {
                ArchiveReceived(context, new("上舰", message.Uid.ToString(CultureInfo.InvariantCulture), message.UserName,
                    $"{message.GuardLevelName} ×{message.Quantity}", receivedAt, message.Timestamp));
                AddGuardPurchase(message);
            }
        });
    }

    internal void AddGuardPurchase(BilibiliGuardPurchaseMessage message)
    {
        if (message.GuardLevel is not (1 or 2 or 3) || message.Uid <= 0)
        {
            return;
        }

        var identity = message.Identity;

        if (!string.IsNullOrWhiteSpace(identity))
        {
            if (!recentGuardPurchaseIdentities.Add(identity))
            {
                return;
            }

            recentGuardPurchaseIdentityOrder.Enqueue(identity);

            while (recentGuardPurchaseIdentityOrder.Count > MAX_RECENT_SUPER_CHAT_IDENTITIES)
            {
                recentGuardPurchaseIdentities.Remove(recentGuardPurchaseIdentityOrder.Dequeue());
            }
        }

        SuperChats.Insert(0, new()
        {
            Kind = LivePaidEventKind.GuardPurchase,
            MessageId = message.MessageId,
            Time = message.Timestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(message.Timestamp) : DateTimeOffset.Now,
            UserName = message.UserName,
            GuardLevel = message.GuardLevel,
            BorderColor = GuardPurchaseStyleService.GetBorderColor(message.GuardLevel)
        });
        Trim(SuperChats, 100);
    }

    private static Color CreateSuperChatBorderColor(string colorText)
    {
        return Color.TryParse(colorText, out var color) ? color : Color.Parse(DEFAULT_SUPER_CHAT_COLOR);
    }


}
