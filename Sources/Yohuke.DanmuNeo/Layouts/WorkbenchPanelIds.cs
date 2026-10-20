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
    internal const string MODERATION = "moderation";
    internal const string ACCOUNT_MEDALS = "accountMedals";
    internal const string LIVE_CAPTURES = "liveCaptures";
    internal const string LIVE_MARKER = "liveMarker";

    private static readonly IReadOnlyList<string> VERSION_ONE = Array.AsReadOnly(new[]
    {
        WORKSPACE, LIVE_PLAYER, COMPOSER, SUPER_CHAT, DANMU, HISTORY, LYRICS, FORWARDING
    });

    private static readonly IReadOnlyList<string> VERSION_TWO = Array.AsReadOnly(new[]
    {
        WORKSPACE, LIVE_PLAYER, COMPOSER, SUPER_CHAT, DANMU, HISTORY, LYRICS, FORWARDING, LOG
    });

    private static readonly IReadOnlyList<string> VERSION_THREE = Array.AsReadOnly(new[]
    {
        WORKSPACE, LIVE_PLAYER, COMPOSER, SUPER_CHAT, DANMU, HISTORY, LYRICS, FORWARDING, LOG, MODERATION
    });

    private static readonly IReadOnlyList<string> VERSION_FOUR = Array.AsReadOnly(new[]
    {
        WORKSPACE, LIVE_PLAYER, COMPOSER, SUPER_CHAT, DANMU, HISTORY, LYRICS, FORWARDING, LOG, MODERATION, ACCOUNT_MEDALS
    });

    private static readonly IReadOnlyList<string> VERSION_FIVE = Array.AsReadOnly(new[]
    {
        WORKSPACE, LIVE_PLAYER, COMPOSER, SUPER_CHAT, DANMU, HISTORY, LYRICS, FORWARDING, LOG, MODERATION,
        ACCOUNT_MEDALS, LIVE_CAPTURES
    });

    private static readonly IReadOnlyList<string> VERSION_SIX = Array.AsReadOnly(new[]
    {
        WORKSPACE, LIVE_PLAYER, COMPOSER, SUPER_CHAT, DANMU, HISTORY, LYRICS, FORWARDING, LOG, MODERATION,
        ACCOUNT_MEDALS, LIVE_CAPTURES, LIVE_MARKER
    });

    internal static IReadOnlyList<string> All => ForVersion(WorkspaceLayoutFile.CURRENT_VERSION);

    internal static IReadOnlyList<string> ForVersion(int version)
    {
        return version switch
        {
            1 => VERSION_ONE,
            2 => VERSION_TWO,
            3 => VERSION_THREE,
            4 => VERSION_FOUR,
            5 => VERSION_FIVE,
            6 => VERSION_SIX,
            _ => []
        };
    }
}
