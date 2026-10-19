using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.ViewModels;

internal sealed record ModerationAccountSnapshot(BilibiliAccount Account, string Id, string Cookie, long Version);
