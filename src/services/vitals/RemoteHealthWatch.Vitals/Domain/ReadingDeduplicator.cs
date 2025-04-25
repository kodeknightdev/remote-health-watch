namespace RemoteHealthWatch.Vitals.Domain;

public sealed record DedupResult<T>(IReadOnlyList<T> Accepted, IReadOnlyList<T> Duplicates, IReadOnlySet<long> LateSeqs);

/// <summary>
/// Devices resend their unacknowledged buffer after a reconnect, so the same (deviceId, seq) can arrive several times
/// and older sequences can arrive after newer ones. Duplicates are dropped; late ones are kept but flagged.
/// </summary>
public static class ReadingDeduplicator
{
    public static DedupResult<T> Split<T>(IEnumerable<T> batch, Func<T, long> seqOf, IReadOnlySet<long> alreadyStored, long? highestStoredSeq)
    {
        var accepted = new List<T>();
        var duplicates = new List<T>();
        var late = new HashSet<long>();
        var seen = new HashSet<long>();
        var high = highestStoredSeq ?? long.MinValue;

        foreach (var reading in batch.OrderBy(seqOf))
        {
            var seq = seqOf(reading);
            if (alreadyStored.Contains(seq) || !seen.Add(seq))
            {
                duplicates.Add(reading);
                continue;
            }
            if (seq < high) late.Add(seq);
            accepted.Add(reading);
        }
        return new DedupResult<T>(accepted, duplicates, late);
    }
}
