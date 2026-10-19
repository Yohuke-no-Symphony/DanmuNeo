using Yohuke.DanmuNeo.Apis.Models.Bilibili;

namespace Yohuke.DanmuNeo.Services;

internal sealed class DanmuHistoryReconciler
{
    private const int CAPACITY = 600;
    private readonly object gate = new();
    private readonly Queue<DanmuHistoryObservation> observations = new();

    internal bool AcceptLive(BilibiliDanmuMessage message)
    {
        lock (gate)
        {
            var history = observations.FirstOrDefault(item => item.IsHistory && !item.MatchedLive && Matches(item.Message, message));
            if (history is not null)
            {
                history.MatchedLive = true;
                return false;
            }

            Add(message, false);
            return true;
        }
    }

    internal IReadOnlyList<BilibiliDanmuMessage> AcceptHistory(IEnumerable<BilibiliDanmuMessage> messages)
    {
        lock (gate)
        {
            var used = new HashSet<DanmuHistoryObservation>();
            var accepted = new List<BilibiliDanmuMessage>();
            foreach (var message in messages)
            {
                var match = observations.FirstOrDefault(item => !used.Contains(item) && Matches(item.Message, message));
                if (match is not null)
                {
                    used.Add(match);
                    continue;
                }

                var added = Add(message, true);
                used.Add(added);
                accepted.Add(message);
            }

            return accepted;
        }
    }

    internal void Clear()
    {
        lock (gate)
        {
            observations.Clear();
        }
    }

    private DanmuHistoryObservation Add(BilibiliDanmuMessage message, bool history)
    {
        var observation = new DanmuHistoryObservation(message, history);
        observations.Enqueue(observation);
        while (observations.Count > CAPACITY)
        {
            observations.Dequeue();
        }

        return observation;
    }

    private static bool Matches(BilibiliDanmuMessage first, BilibiliDanmuMessage second)
    {
        if (first.RoomId != second.RoomId)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(first.MessageId) && !string.IsNullOrWhiteSpace(second.MessageId))
        {
            return first.MessageId == second.MessageId;
        }

        return first.Timestamp > 0 && second.Timestamp == first.Timestamp && first.Uid > 0 &&
               first.Uid == second.Uid && first.Content == second.Content;
    }
}
