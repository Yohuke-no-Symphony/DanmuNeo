using Yohuke.DanmuNeo.Models.State;

namespace Yohuke.DanmuNeo.Services;

internal sealed record MedalSwitchTarget(BilibiliAccount Account, string Cookie, string RoomId, long OwnerUid,
    long Revision);
