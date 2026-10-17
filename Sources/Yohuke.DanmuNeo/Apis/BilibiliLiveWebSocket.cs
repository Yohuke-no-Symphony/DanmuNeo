using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Yohuke.DanmuNeo.Apis.Models.Bilibili;
using Yohuke.DanmuNeo.Apis.Models.Common;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Apis;

/// <summary>
/// B 站直播弹幕 WebSocket 客户端。
/// </summary>
public class BilibiliLiveWebSocket : BaseApi, ILiveDanmuSocket
{
    private const string DEFAULT_SUPER_CHAT_COLOR = "#2A60B2";
    private const int HEARTBEAT_INTERVAL_SECONDS = 30;
    private const int CONNECTION_MONITOR_INTERVAL_SECONDS = 5;
    private const int CONNECTION_TIMEOUT_SECONDS = 75;
    private const long MAX_UNIX_TIMESTAMP_SECONDS = 253402300799;
    private const string URL_GET_DANMU_INFO = "https://api.live.bilibili.com/xlive/web-room/v1/index/getDanmuInfo";

    private static readonly Regex TL_PATTERN1 =
        new(@"^【(?<speaker>[^:：]{1,5})[:：](?<content>[^】]+)", RegexOptions.Compiled);

    private static readonly Regex TL_PATTERN2 =
        new(@"^(?<speaker>[^\u0592✉【][^【]{0,4})?【(?<content>[^】]+)", RegexOptions.Compiled);

    private static readonly byte[] HEARTBEAT_PACKET = Convert.FromHexString("00000010001000010000000200000001");

    private readonly string roomId;
    private readonly BilibiliLoginCookie cookie;
    private readonly BilibiliWbiSigner signer = new();

    /// <summary>
    /// 接收到翻译弹幕时触发。
    /// </summary>
    public event EventHandler<BilibiliTranslatedDanmuMessage>? TranslatedDanmuReceived;

    /// <summary>
    /// 接收到普通弹幕时触发。
    /// </summary>
    public event EventHandler<BilibiliDanmuMessage>? DanmuReceived;

    /// <summary>
    /// 接收到金币礼物时触发。
    /// </summary>
    public event EventHandler<BilibiliGiftMessage>? GiftReceived;

    /// <summary>
    /// 接收到 Super Chat 时触发。
    /// </summary>
    public event EventHandler<BilibiliSuperChatMessage>? SuperChatReceived;

    /// <summary>
    /// 接收到大航海购买或续费记录时触发。
    /// </summary>
    public event EventHandler<BilibiliGuardPurchaseMessage>? GuardPurchaseReceived;

    /// <summary>
    /// 监听发生错误时触发。
    /// </summary>
    public event EventHandler<Exception>? ErrorReceived;

    /// <summary>
    /// 连接中断时触发。
    /// </summary>
    public event EventHandler? Disconnected;

    /// <summary>
    /// 连接恢复时触发。
    /// </summary>
    public event EventHandler? Recovered;

    /// <summary>
    /// 初始化直播弹幕 WebSocket。
    /// </summary>
    public BilibiliLiveWebSocket(long roomId, string cookie, TimeSpan? timeout = null)
        : this(roomId.ToString(), CreateCookie(cookie), timeout)
    {
    }

    /// <summary>
    /// 初始化直播弹幕 WebSocket。
    /// </summary>
    public BilibiliLiveWebSocket(string roomId, BilibiliLoginCookie cookie, TimeSpan? timeout = null)
        : base(timeout: timeout ?? TimeSpan.FromSeconds(5))
    {
        this.roomId = roomId;
        this.cookie = cookie;
    }

    /// <summary>
    /// 开始监听直播间弹幕，直到取消令牌被取消。
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var reconnectCoordinator = new LiveDanmuReconnectCoordinator(ConnectOnceAsync);
        reconnectCoordinator.ErrorReceived += (_, exception) =>
        {
            ErrorReceived?.Invoke(this, exception);
        };
        reconnectCoordinator.Disconnected += (_, _) => Disconnected?.Invoke(this, EventArgs.Empty);
        reconnectCoordinator.Recovered += (_, _) => Recovered?.Invoke(this, EventArgs.Empty);
        await reconnectCoordinator.RunAsync(cancellationToken);
    }

    /// <summary>
    /// 获取直播间弹幕服务器信息。
    /// </summary>
    public async Task<ApiResponse<BilibiliDanmuInfoData>> GetDanmuInfoAsync(
        CancellationToken cancellationToken = default)
    {
        var parameters = await signer.FillAsync(new Dictionary<string, object?>
        {
            ["id"] = roomId,
            ["type"] = 0,
            ["web_location"] = 444.8
        }, cancellationToken);

        return await GetJsonAsync<ApiResponse<BilibiliDanmuInfoData>>(
            URL_GET_DANMU_INFO,
            parameters,
            CreateHeaders(),
            cancellationToken: cancellationToken);
    }

    private async Task ConnectOnceAsync(Action onConnected, CancellationToken cancellationToken)
    {
        var danmuInfo = await GetDanmuInfoAsync(cancellationToken);
        var data = danmuInfo.Data ?? throw new InvalidOperationException("没有获取到弹幕服务器数据。");
        var host = data.HostList.FirstOrDefault() ?? throw new InvalidOperationException("没有可用的弹幕服务器。");
        var uri = CreateWebSocketUri(host);

        using var websocket = new ClientWebSocket();
        using var connectionTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var connectionToken = connectionTokenSource.Token;
        websocket.Options.SetRequestHeader("User-Agent", DEFAULT_USER_AGENT);
        websocket.Options.SetRequestHeader("Origin", "https://live.bilibili.com");
        websocket.Options.SetRequestHeader("Cookie", cookie.ToCookieString());
        await websocket.ConnectAsync(uri, connectionToken);

        var enterRoomPacket = CreateEnterRoomPacket(data.Token);
        await websocket.SendAsync(enterRoomPacket, WebSocketMessageType.Binary, true, connectionToken);
        var lastPacketTimestamp = Stopwatch.GetTimestamp();
        var receiveTask = ReceiveLoopAsync(
            websocket,
            () => Interlocked.Exchange(ref lastPacketTimestamp, Stopwatch.GetTimestamp()),
            connectionToken);
        var heartbeatTask = SendHeartbeatAsync(websocket, connectionToken);
        var monitorTask = MonitorConnectionAsync(
            () => Interlocked.Read(ref lastPacketTimestamp),
            connectionToken);
        onConnected();

        try
        {
            var completedTask = await Task.WhenAny(receiveTask, heartbeatTask, monitorTask);
            await completedTask;

            if (!connectionToken.IsCancellationRequested)
            {
                throw new WebSocketException("直播弹幕连接已关闭。");
            }
        }
        finally
        {
            connectionTokenSource.Cancel();
            websocket.Abort();
            await IgnoreConnectionTaskErrorsAsync(receiveTask, heartbeatTask, monitorTask);
        }
    }

    private async Task SendHeartbeatAsync(ClientWebSocket websocket, CancellationToken cancellationToken)
    {
        while (websocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            await websocket.SendAsync(HEARTBEAT_PACKET, WebSocketMessageType.Binary, true, cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(HEARTBEAT_INTERVAL_SECONDS), cancellationToken);
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            throw new WebSocketException("直播弹幕心跳已停止。");
        }
    }

    private async Task ReceiveLoopAsync(
        ClientWebSocket websocket,
        Action onPacketReceived,
        CancellationToken cancellationToken)
    {
        while (websocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var packet = await ReceivePacketAsync(websocket, cancellationToken);

            if (packet.Length == 0)
            {
                throw new WebSocketException("直播弹幕服务器已关闭连接。");
            }

            onPacketReceived();
            AnalysePackage(packet);
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            throw new WebSocketException("直播弹幕接收已停止。");
        }
    }

    private static async Task MonitorConnectionAsync(
        Func<long> getLastPacketTimestamp,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(CONNECTION_MONITOR_INTERVAL_SECONDS), cancellationToken);

            if (Stopwatch.GetElapsedTime(getLastPacketTimestamp()) <= TimeSpan.FromSeconds(CONNECTION_TIMEOUT_SECONDS))
            {
                continue;
            }

            throw new TimeoutException("直播弹幕连接长时间未收到心跳响应。");
        }
    }

    private static async Task IgnoreConnectionTaskErrorsAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch
        {
        }
    }

    private static async Task<byte[]> ReceivePacketAsync(ClientWebSocket websocket, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        using var stream = new MemoryStream();

        while (true)
        {
            var result = await websocket.ReceiveAsync(buffer, cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return [];
            }

            stream.Write(buffer, 0, result.Count);

            if (result.EndOfMessage)
            {
                return stream.ToArray();
            }
        }
    }

    private void AnalysePackage(byte[] rawData)
    {
        if (rawData.Length < 16)
        {
            return;
        }

        var packageLength = BinaryPrimitives.ReadInt32BigEndian(rawData.AsSpan(0, 4));
        var version = BinaryPrimitives.ReadInt16BigEndian(rawData.AsSpan(6, 2));
        var operation = BinaryPrimitives.ReadInt32BigEndian(rawData.AsSpan(8, 4));

        if (operation == 3)
        {
            return;
        }

        if (rawData.Length > packageLength && packageLength > 0)
        {
            AnalysePackage(rawData[..packageLength]);
            AnalysePackage(rawData[packageLength..]);
            return;
        }

        if (version == 3)
        {
            AnalysePackage(DecompressBrotli(rawData[16..]));
            return;
        }

        if ((version == 0 || version == 1) && operation == 5)
        {
            AnalyseBusinessPackage(rawData[16..]);
        }
    }

    internal void AnalyseBusinessPackage(byte[] body)
    {
        var text = Encoding.UTF8.GetString(body);
        var json = JObject.Parse(text);
        var command = json.Value<string>("cmd");

        if (IsCommand(command, "DANMU_MSG"))
        {
            var danmu = ParseDanmuMessage(roomId, json);

            if (danmu is null)
            {
                return;
            }

            DanmuReceived?.Invoke(this, danmu);
            PublishTranslatedDanmu(danmu.Content);
            return;
        }

        if (IsCommand(command, "SEND_GIFT"))
        {
            var gift = ParseGiftMessage(roomId, json);

            if (gift is not null)
            {
                GiftReceived?.Invoke(this, gift);
            }

            return;
        }

        if (IsCommand(command, "SUPER_CHAT_MESSAGE"))
        {
            var superChat = ParseSuperChatMessage(roomId, json);

            if (superChat is not null)
            {
                SuperChatReceived?.Invoke(this, superChat);
            }

            return;
        }

        if (IsCommand(command, "GUARD_BUY") || IsCommand(command, "USER_TOAST_MSG"))
        {
            var purchase = ParseGuardPurchaseMessage(roomId, json);

            if (purchase is not null)
            {
                GuardPurchaseReceived?.Invoke(this, purchase);
            }
        }
    }

    /// <summary>
    /// 从业务 JSON 中解析普通弹幕。
    /// </summary>
    public static BilibiliDanmuMessage? ParseDanmuMessage(string roomId, JObject json)
    {
        var info = json["info"] as JArray;

        if (info is null)
        {
            return null;
        }

        var metadata = ReadArrayItem(info, 0);
        var legacyUser = ReadArrayItem(info, 2);
        var modernUser = ReadObjectProperty(ReadArrayItem(metadata, 15), "user");
        var modernMedal = ReadObjectProperty(modernUser, "medal") as JObject;
        var legacyMedal = ReadArrayItem(info, 3);
        var medalGuardLevel = ReadNonNegativeInt(modernMedal is not null
            ? modernMedal["guard_level"]
            : ReadArrayItem(legacyMedal, 10));
        var modernUid = ReadInteger(ReadObjectProperty(modernUser, "uid"));
        var modernName = ReadScalarText(ReadObjectProperty(ReadObjectProperty(modernUser, "base"), "name"));
        var content = ReadScalarText(ReadArrayItem(info, 1));

        return new()
        {
            RoomId = roomId,
            Uid = modernUid > 0 ? modernUid : ReadInteger(ReadArrayItem(legacyUser, 0)),
            UserName = string.IsNullOrWhiteSpace(modernName)
                ? ReadScalarText(ReadArrayItem(legacyUser, 1))
                : modernName,
            Content = content,
            IsRoomAdmin = ReadBoolean(ReadArrayItem(legacyUser, 2)),
            MedalName = modernMedal is not null
                ? ReadScalarText(modernMedal["name"])
                : ReadScalarText(ReadArrayItem(legacyMedal, 1)),
            MedalLevel = ReadNonNegativeInt(modernMedal is not null
                ? modernMedal["level"]
                : ReadArrayItem(legacyMedal, 0)),
            MedalGuardLevel = medalGuardLevel is >= 1 and <= 3 ? medalGuardLevel : 0,
            UserLevel = ReadNonNegativeInt(ReadArrayItem(ReadArrayItem(info, 4), 0)),
            IsEmoticon = IsProtocolEmoticon(metadata, content),
            Emoticons = ParseDanmuEmoticons(metadata, content)
        };
    }

    private static List<BilibiliDanmuEmoticon> ParseDanmuEmoticons(JToken? metadata, string content)
    {
        var result = new List<BilibiliDanmuEmoticon>();
        var danmuExtra = ReadDanmuExtra(metadata);
        var emots = danmuExtra?["emots"] as JObject ??
                    ReadObjectProperty(ReadArrayItem(metadata, 15), "emots") as JObject;

        if (emots is not null)
        {
            foreach (var emoticon in emots.Properties())
            {
                var text = emoticon.Name;

                if (string.IsNullOrWhiteSpace(text) || !content.Contains(text, StringComparison.Ordinal))
                {
                    text = ReadScalarText(ReadObjectProperty(emoticon.Value, "text"));
                }

                AddDanmuEmoticon(result, content, text, ReadScalarText(ReadObjectProperty(emoticon.Value, "url")));
            }
        }

        if (result.Count == 0 && IsProtocolEmoticon(metadata, content))
        {
            var emoticon = ReadArrayItem(metadata, 13);
            var url = ReadScalarText(ReadObjectProperty(emoticon, "url"));

            if (string.IsNullOrWhiteSpace(url))
            {
                url = ReadScalarText(ReadObjectProperty(ReadObjectProperty(danmuExtra, "emoticon"), "url"));
            }

            AddDanmuEmoticon(result, content, content.Trim(), url);
        }

        return result;
    }

    private static JObject? ReadDanmuExtra(JToken? metadata)
    {
        var extra = ReadObjectProperty(ReadArrayItem(metadata, 15), "extra");

        if (extra is JObject extraObject)
        {
            return extraObject;
        }

        var serializedExtra = ReadScalarText(extra);

        if (string.IsNullOrWhiteSpace(serializedExtra))
        {
            return null;
        }

        try
        {
            return JObject.Parse(serializedExtra);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void AddDanmuEmoticon(
        ICollection<BilibiliDanmuEmoticon> emoticons,
        string content,
        string text,
        string url)
    {
        text = text.Trim();
        url = NormalizeEmoticonUrl(url);

        if (string.IsNullOrWhiteSpace(text) ||
            !content.Contains(text, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(url) ||
            emoticons.Any(emoticon => string.Equals(emoticon.Text, text, StringComparison.Ordinal)))
        {
            return;
        }

        emoticons.Add(new()
        {
            Text = text,
            Url = url
        });
    }

    private static string NormalizeEmoticonUrl(string url)
    {
        url = url.Trim();

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            url = $"https:{url}";
        }
        else if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            url = $"https://{url["http://".Length..]}";
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps
            ? uri.AbsoluteUri
            : "";
    }

    /// <summary>
    /// 从旧购买消息或大航海提示消息中解析购买、续费记录。
    /// </summary>
    public static BilibiliGuardPurchaseMessage? ParseGuardPurchaseMessage(string roomId, JObject json)
    {
        var command = json.Value<string>("cmd");

        if ((!IsCommand(command, "GUARD_BUY") && !IsCommand(command, "USER_TOAST_MSG")) ||
            json["data"] is not JObject data)
        {
            return null;
        }

        var isModern = IsCommand(command, "USER_TOAST_MSG_V2");
        var option = data["option"];

        if (isModern && ReadInteger(ReadObjectProperty(option, "source")) == 2)
        {
            return null;
        }

        var sender = data["sender_uinfo"];
        var guard = data["guard_info"];
        var pay = data["pay_info"];
        var uid = ReadInteger(isModern ? ReadObjectProperty(sender, "uid") : data["uid"]);
        var name = ReadScalarText(isModern
            ? ReadObjectProperty(ReadObjectProperty(sender, "base"), "name")
            : data["username"] ?? data["uname"]);
        var guardLevel = ReadNonNegativeInt(isModern ? ReadObjectProperty(guard, "guard_level") : data["guard_level"]);

        if (uid <= 0 || string.IsNullOrWhiteSpace(name) || guardLevel is not (1 or 2 or 3))
        {
            return null;
        }

        return new()
        {
            MessageId = ReadScalarText(data["msg_id"] ?? data["id"] ?? json["msg_id"]),
            RoomId = roomId,
            Uid = uid,
            UserName = name,
            GuardLevel = guardLevel,
            Timestamp = ReadServerTimestamp(
                isModern ? ReadObjectProperty(guard, "start_time") : data["start_time"],
                data["timestamp"],
                data["ts"],
                json["ts"],
                json["send_time"]),
            Quantity = ReadNonNegativeInt(isModern ? ReadObjectProperty(pay, "num") : data["num"]),
            IsGroup = ReadBoolean(ReadObjectProperty(option, "is_group")),
            ReceiverUid = ReadInteger(ReadObjectProperty(data["receiver_uinfo"], "uid"))
        };
    }

    /// <summary>
    /// 从业务 JSON 中解析金币礼物。
    /// </summary>
    public static BilibiliGiftMessage? ParseGiftMessage(string roomId, JObject json)
    {
        if (!IsCommand(json.Value<string>("cmd"), "SEND_GIFT") || json["data"] is not JObject data ||
            !string.Equals(ReadScalarText(data["coin_type"]), "gold", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var sender = data["sender_uinfo"];
        var senderBase = ReadObjectProperty(sender, "base");
        var giftInfo = data["gift_info"];
        var uid = ReadInteger(ReadObjectProperty(sender, "uid"));
        var userName = ReadScalarText(ReadObjectProperty(senderBase, "name"));
        var giftName = ReadScalarText(data["giftName"]);
        var quantity = ReadNonNegativeInt(data["super_gift_num"]);
        var batchComboCount = ReadNonNegativeInt(ReadObjectProperty(data["batch_combo_send"], "batch_combo_num"));

        if (uid <= 0)
        {
            uid = ReadInteger(data["uid"]);
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            userName = ReadScalarText(data["uname"]);
        }

        if (string.IsNullOrWhiteSpace(giftName))
        {
            giftName = ReadScalarText(ReadObjectProperty(giftInfo, "gift_name"));
        }

        if (quantity <= 0)
        {
            quantity = ReadNonNegativeInt(data["num"]);
        }

        if (quantity <= 0)
        {
            quantity = batchComboCount;
        }

        if (uid <= 0 || string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(giftName) || quantity <= 0)
        {
            return null;
        }

        return new()
        {
            MessageId = ReadScalarText(data["tid"]),
            RoomId = roomId,
            Uid = uid,
            UserName = userName,
            GiftId = ReadScalarText(data["giftId"] ?? ReadObjectProperty(giftInfo, "gift_id")),
            GiftName = giftName,
            Quantity = quantity,
            BatchComboId = ReadScalarText(data["batch_combo_id"]),
            BatchComboCount = batchComboCount,
            UnitPrice = ReadDecimal(data["price"]),
            Timestamp = ReadServerTimestamp(data["timestamp"], data["ts"], json["send_time"])
        };
    }

    private static JToken? ReadArrayItem(JToken? token, int index)
    {
        return token is JArray array && index >= 0 && index < array.Count ? array[index] : null;
    }

    private static JToken? ReadObjectProperty(JToken? token, string name)
    {
        return token is JObject value ? value[name] : null;
    }

    private static string ReadScalarText(JToken? token)
    {
        return token is JValue ? ReadTokenText(token) : "";
    }

    private static long ReadInteger(JToken? token)
    {
        return long.TryParse(ReadScalarText(token), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static int ReadNonNegativeInt(JToken? token)
    {
        var value = ReadInteger(token);
        return value is >= 0 and <= int.MaxValue ? (int)value : 0;
    }

    private static decimal ReadDecimal(JToken? token)
    {
        return decimal.TryParse(ReadScalarText(token), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? Math.Max(0, value)
            : 0;
    }

    private static bool ReadBoolean(JToken? token)
    {
        return token?.Type == JTokenType.Boolean ? token.Value<bool>() : ReadInteger(token) > 0;
    }

    private static long ReadServerTimestamp(params JToken?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            var value = ReadInteger(candidate);

            if (value > MAX_UNIX_TIMESTAMP_SECONDS)
            {
                value /= 1000;
            }

            if (value is > 0 and <= MAX_UNIX_TIMESTAMP_SECONDS)
            {
                return value;
            }
        }

        return 0;
    }

    private static bool IsProtocolEmoticon(JToken? metadata, string? content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Contains('\r') || content.Contains('\n'))
        {
            return false;
        }

        if (ReadBoolean(ReadArrayItem(metadata, 12)))
        {
            return true;
        }

        var emoticonUnique = (metadata as JContainer)?.Descendants()
            .OfType<JProperty>()
            .FirstOrDefault(property =>
                string.Equals(property.Name, "emoticon_unique", StringComparison.OrdinalIgnoreCase))
            ?.Value.Value<string>();

        if (string.IsNullOrWhiteSpace(emoticonUnique))
        {
            return false;
        }

        return Regex.IsMatch(content.Trim(), @"^\[[^\[\]\r\n]+\]$", RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// 从业务 JSON 中解析 Super Chat。
    /// </summary>
    public static BilibiliSuperChatMessage? ParseSuperChatMessage(string roomId, JObject json)
    {
        var data = json["data"];

        if (data is null)
        {
            return null;
        }

        var price = data["price"]?.Value<decimal>() ?? 0;
        var priceText = data["price_text"]?.Value<string>();

        return new()
        {
            MessageId = ReadTokenText(data["id"]),
            RoomId = roomId,
            UserName = data["user_info"]?["uname"]?.Value<string>() ?? "",
            Price = price,
            PriceText = string.IsNullOrWhiteSpace(priceText)
                ? $"￥{price.ToString("0.##", CultureInfo.InvariantCulture)}"
                : priceText,
            BorderColor = ReadSuperChatColor(data),
            Content = data["message"]?.Value<string>() ?? "",
            Timestamp = data["ts"]?.Value<long>() ?? DateTimeOffset.Now.ToUnixTimeSeconds()
        };
    }

    private static string ReadSuperChatColor(JToken data)
    {
        string[] colorKeys =
        [
            "background_color_start",
            "background_bottom_color",
            "background_price_color"
        ];

        foreach (var colorKey in colorKeys)
        {
            var color = data[colorKey]?.Value<string>();

            if (IsHexColor(color))
            {
                return color!;
            }
        }

        return DEFAULT_SUPER_CHAT_COLOR;
    }

    private static bool IsHexColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] != '#' || value.Length is not (7 or 9))
        {
            return false;
        }

        return value[1..].All(Uri.IsHexDigit);
    }

    private static string ReadTokenText(JToken? token)
    {
        return token switch
        {
            null => "",
            JValue value => Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? "",
            _ => token.ToString(Formatting.None)
        };
    }

    /// <summary>
    /// 根据弹幕服务器信息创建 WebSocket 地址。
    /// </summary>
    public static Uri CreateWebSocketUri(BilibiliDanmuHost host)
    {
        var port = host.WssPort > 0 ? $":{host.WssPort}" : "";
        return new($"wss://{host.Host}{port}/sub");
    }

    /// <summary>
    /// 判断业务命令是否匹配指定前缀。
    /// </summary>
    public static bool IsCommand(string? command, string expectedCommand)
    {
        return command?.StartsWith(expectedCommand, StringComparison.Ordinal) == true;
    }

    private void PublishTranslatedDanmu(string rawContent)
    {
        var match = TL_PATTERN1.Match(rawContent);

        if (!match.Success)
        {
            match = TL_PATTERN2.Match(rawContent);
        }

        if (match.Success)
        {
            TranslatedDanmuReceived?.Invoke(this, new()
            {
                RoomId = roomId,
                Speaker = match.Groups["speaker"].Success ? match.Groups["speaker"].Value : "",
                Content = match.Groups["content"].Value,
                RawContent = rawContent
            });
        }
    }

    private static byte[] DecompressBrotli(byte[] data)
    {
        var outputBuffer = new byte[Math.Max(data.Length * 8, 8192)];

        while (true)
        {
            var decoder = new BrotliDecoder();
            var status = decoder.Decompress(data, outputBuffer, out _, out var bytesWritten);

            if (status == OperationStatus.Done)
            {
                return outputBuffer[..bytesWritten];
            }

            if (status == OperationStatus.DestinationTooSmall)
            {
                outputBuffer = new byte[outputBuffer.Length * 2];
                continue;
            }

            throw new InvalidDataException("Brotli 弹幕包解压失败。");
        }
    }

    private byte[] CreateEnterRoomPacket(string token)
    {
        var payload = new
        {
            uid = long.TryParse(cookie.DedeUserId, out var uid) ? uid : 0,
            roomid = long.TryParse(roomId, out var parsedRoomId) ? parsedRoomId : 0,
            protover = 3,
            platform = "web",
            buvid = cookie.Buvid3,
            type = 2,
            key = token
        };
        var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(payload));
        return CreatePacket(body, 7);
    }

    private static byte[] CreatePacket(byte[] body, int operation)
    {
        var packet = new byte[16 + body.Length];
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(0, 4), packet.Length);
        BinaryPrimitives.WriteInt16BigEndian(packet.AsSpan(4, 2), 16);
        BinaryPrimitives.WriteInt16BigEndian(packet.AsSpan(6, 2), 1);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(8, 4), operation);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(12, 4), 1);
        body.CopyTo(packet.AsSpan(16));
        return packet;
    }

    private Dictionary<string, string> CreateHeaders()
    {
        var headers = CreateDefaultHeaders();
        headers["Cookie"] = cookie.ToCookieString();
        return headers;
    }

    private static BilibiliLoginCookie CreateCookie(string cookie)
    {
        var normalizedCookie = ApiCookieHelper.CreateBilibiliCookie(cookie);

        return new()
        {
            Buvid3 = ApiCookieHelper.GetValue(normalizedCookie, "buvid3"),
            SessData = ApiCookieHelper.GetValue(normalizedCookie, "SESSDATA"),
            BiliJct = ApiCookieHelper.GetValue(normalizedCookie, "bili_jct"),
            DedeUserId = ApiCookieHelper.GetValue(normalizedCookie, "DedeUserId")
        };
    }
}
