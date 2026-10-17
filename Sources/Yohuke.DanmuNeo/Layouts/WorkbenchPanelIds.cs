namespace Yohuke.DanmuNeo.Layouts;

internal static class WorkbenchPanelIds
{
    internal const string WORKSPACE = "workspace";
    internal const string LIVE_PLAYER = "livePlayer";
    internal const string COMPOSER = "composer";
    internal const string SUPER_CHAT = "superChat";
    internal const string DANMU = "danmu";
    internal const string HISTORY = "history";
    internal const string LYRICS = "lyrics";
    internal const string FORWARDING = "forwarding";
    internal const string LOG = "log";

    internal static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        WORKSPACE, LIVE_PLAYER, COMPOSER, SUPER_CHAT, DANMU, HISTORY, LYRICS, FORWARDING, LOG
    });
}
