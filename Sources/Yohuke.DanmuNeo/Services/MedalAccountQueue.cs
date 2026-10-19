namespace Yohuke.DanmuNeo.Services;

internal sealed class MedalAccountQueue
{
    internal SemaphoreSlim Serial { get; } = new(1, 1);
    internal SemaphoreSlim PreferenceSerial { get; } = new(1, 1);
    internal bool PreferenceSaving { get; set; }
    internal MedalSwitchTarget? Pending { get; set; }
    internal MedalPendingWearingChange? PendingWearingChange { get; set; }
    internal bool Running { get; set; }
    internal Task Worker { get; set; } = Task.CompletedTask;
    internal long LastQueuedRevision { get; set; } = -1;
    internal long LastQueuedUid { get; set; }
}
