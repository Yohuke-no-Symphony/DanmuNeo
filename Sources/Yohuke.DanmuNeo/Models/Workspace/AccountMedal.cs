namespace Yohuke.DanmuNeo.Models.Workspace;

/// <summary>账号拥有的粉丝牌，佩戴状态只属于运行时。</summary>
public sealed record AccountMedal(long Id, long TargetUid, string Name, int Level, bool IsWearing,
    string TargetName = "");
