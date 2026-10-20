namespace Yohuke.DanmuNeo.Models.LiveMarker;

internal sealed record LiveMarkerEdit(int Index, LiveMarkerEntry? Before, LiveMarkerEntry? After,
    long BeforeNode, long AfterNode);
