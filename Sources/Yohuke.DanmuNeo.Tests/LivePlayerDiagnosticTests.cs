using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using Yohuke.DanmuNeo.Models.Logging;
using Yohuke.DanmuNeo.Services;

namespace Yohuke.DanmuNeo.Tests;

/// <summary>
/// 通过离线 loopback 验证网页播放器的错误、恢复和失效会话隔离。
/// </summary>
public class LivePlayerDiagnosticTests
{
    /// <summary>
    /// 同一故障去重，级别升级与恢复后再次故障可记录，撤销令牌后拒绝迟到报告。
    /// </summary>
    [Fact]
    public async Task DiagnosticsKeepRoomAndRejectRevokedSessions()
    {
        using var service = new LivePlayerService();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var room = Guid.NewGuid().ToString("N");
        var records = new ConcurrentQueue<(AppLogLevel Level, string Message)>();

        void Capture(AppLogLevel level, string source, string message, Exception? error)
        {
            if (source == $"播放器/房间 {room}")
            {
                records.Enqueue((level, message));
            }
        }

        StartupLog.Written += Capture;

        try
        {
            var session = await service.CreatePlayerAsync(new()
            {
                RoomId = room, StreamUrl = "https://example.invalid/offline.flv"
            }, "");
            var endpoint = new Uri(new Uri(session.PlayerUrl), $"/diagnostic/{session.Token}");

            foreach (var kind in new[] { "warning", "warning", "error", "recovered", "recovered", "error" })
            {
                using var response = await client.PostAsJsonAsync(endpoint,
                    new { kind, message = kind == "recovered" ? "" : "offline player failure" });
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }

            Assert.Equal(new[] { AppLogLevel.WARNING, AppLogLevel.ERROR, AppLogLevel.INFO, AppLogLevel.ERROR },
                records.Select(record => record.Level));
            using var different = await client.PostAsJsonAsync(endpoint,
                new { kind = "error", message = "second failure" });
            Assert.Equal(HttpStatusCode.NoContent, different.StatusCode);
            using var repeated = await client.PostAsJsonAsync(endpoint,
                new { kind = "error", message = "offline player failure" });
            Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
            Assert.Equal(5, records.Count);
            service.Revoke(session.Token);
            using var late = await client.PostAsJsonAsync(endpoint, new { kind = "error", message = "late" });
            Assert.Equal(HttpStatusCode.NotFound, late.StatusCode);
            Assert.Equal(5, records.Count);
        }
        finally
        {
            StartupLog.Written -= Capture;
        }
    }

    /// <summary>
    /// 无效方法、来源、内容及过大诊断不会进入播放器日志。
    /// </summary>
    [Fact]
    public async Task DiagnosticsValidateTransportAndPayload()
    {
        using var service = new LivePlayerService();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var session = await service.CreatePlayerAsync(new()
        {
            RoomId = "offline", StreamUrl = "https://example.invalid/offline.flv"
        }, "");
        var endpoint = new Uri(new Uri(session.PlayerUrl), $"/diagnostic/{session.Token}");
        using var get = await client.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);
        using var foreign = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new { kind = "error", message = "rejected" })
        };
        foreign.Headers.Add("Origin", "https://example.invalid");
        using var rejected = await client.SendAsync(foreign);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);

        foreach (var body in new[] { "null", "invalid json", "{}", "{\"kind\":\"other\",\"message\":\"text\"}" })
        {
            using var invalid = await client.PostAsync(endpoint, new StringContent(body, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }

        using var large = await client.PostAsync(endpoint, new StringContent(new string('x', 5000)));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, large.StatusCode);
    }
}
