using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RemoteHealthWatch.Contracts;
using RemoteHealthWatch.Vitals.Data;
using RemoteHealthWatch.Vitals.Domain;

namespace RemoteHealthWatch.Vitals.Services;

/// <summary>
/// Recomputes only the buckets a batch touched, from the raw readings (minute) and from the minute rollups (hour).
/// Recomputing instead of incrementing keeps late readings and replays correct.
/// </summary>
public sealed class RollupWriter(VitalsDb db)
{
    public async Task RefreshAsync(Guid patientId, IReadOnlyList<(VitalType Vital, DateTimeOffset At)> readings, CancellationToken ct)
    {
        var minutes = Buckets.Touched(readings, RollupGrain.Minute);
        var hours = Buckets.Touched(readings, RollupGrain.Hour);
        await MergeAsync("Rollup1m", MinuteSource, patientId, minutes, TimeSpan.FromMinutes(1), ct);
        await MergeAsync("Rollup1h", HourSource, patientId, hours, TimeSpan.FromHours(1), ct);
    }

    private const string MinuteSource = """
        SELECT r.PatientId, r.Vital, DATEADD(minute, DATEDIFF(minute, 0, r.MeasuredAt), 0) AS BucketStart,
               COUNT(*) AS [Count], MIN(r.Value) AS MinValue, MAX(r.Value) AS MaxValue, AVG(r.Value) AS AvgValue, AVG(r.Value2) AS AvgValue2
        FROM vitals.Readings r
        WHERE r.PatientId = @p AND ({0})
        GROUP BY r.PatientId, r.Vital, DATEADD(minute, DATEDIFF(minute, 0, r.MeasuredAt), 0)
        """;

    private const string HourSource = """
        SELECT m.PatientId, m.Vital, DATEADD(hour, DATEDIFF(hour, 0, m.BucketStart), 0) AS BucketStart,
               SUM(m.[Count]) AS [Count], MIN(m.MinValue) AS MinValue, MAX(m.MaxValue) AS MaxValue,
               SUM(m.AvgValue * m.[Count]) / SUM(m.[Count]) AS AvgValue,
               SUM(m.AvgValue2 * m.[Count]) / NULLIF(SUM(CASE WHEN m.AvgValue2 IS NULL THEN 0 ELSE m.[Count] END), 0) AS AvgValue2
        FROM vitals.Rollup1m m
        WHERE m.PatientId = @p AND ({0})
        GROUP BY m.PatientId, m.Vital, DATEADD(hour, DATEDIFF(hour, 0, m.BucketStart), 0)
        """;

    private async Task MergeAsync(string table, string sourceTemplate, Guid patientId, IReadOnlyList<(VitalType Vital, DateTime Bucket)> buckets,
        TimeSpan width, CancellationToken ct)
    {
        if (buckets.Count == 0) return;
        var parameters = new List<SqlParameter> { new("@p", patientId) };
        var filter = new StringBuilder();
        var timeColumn = table == "Rollup1m" ? "r.MeasuredAt" : "m.BucketStart";
        var alias = table == "Rollup1m" ? "r" : "m";
        for (var i = 0; i < buckets.Count; i++)
        {
            if (i > 0) filter.Append(" OR ");
            filter.Append($"({alias}.Vital = @v{i} AND {timeColumn} >= @f{i} AND {timeColumn} < @t{i})");
            parameters.Add(new SqlParameter($"@v{i}", (byte)buckets[i].Vital));
            parameters.Add(new SqlParameter($"@f{i}", buckets[i].Bucket));
            parameters.Add(new SqlParameter($"@t{i}", buckets[i].Bucket + width));
        }

        var sql = $"""
            MERGE vitals.{table} WITH (HOLDLOCK) AS target
            USING ({string.Format(sourceTemplate, filter)}) AS source
            ON target.PatientId = source.PatientId AND target.Vital = source.Vital AND target.BucketStart = source.BucketStart
            WHEN MATCHED THEN UPDATE SET [Count] = source.[Count], MinValue = source.MinValue, MaxValue = source.MaxValue,
                AvgValue = source.AvgValue, AvgValue2 = source.AvgValue2
            WHEN NOT MATCHED THEN INSERT (PatientId, Vital, BucketStart, [Count], MinValue, MaxValue, AvgValue, AvgValue2)
                VALUES (source.PatientId, source.Vital, source.BucketStart, source.[Count], source.MinValue, source.MaxValue, source.AvgValue, source.AvgValue2);
            """;
        await db.Database.ExecuteSqlRawAsync(sql, parameters, ct);
    }
}
