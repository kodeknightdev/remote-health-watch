using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RemoteHealthWatch.DeviceSim;

public sealed record SeededDevice(Guid DeviceId, string Serial, string ApiKey, Guid PatientId, string Scenario, int IntervalSeconds);
public sealed record Reading(long Seq, string Vital, double Value, double? Value2, string Unit, DateTimeOffset MeasuredAt);
public sealed record Batch(int? BatteryPercent, IReadOnlyList<Reading> Readings);

public sealed class SimStats
{
    public long Sent;
    public long Accepted;
    public long Rejected;
    public long Throttled;
    public long Failed;
}

/// <summary>One simulated device: samples on its interval, buffers while offline, resends on reconnect like real firmware.</summary>
public sealed class DeviceRunner(SeededDevice device, HttpClient http, SimOptions options, SimStats stats)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly Scenario _scenario = Enum.Parse<Scenario>(device.Scenario);
    private readonly List<Batch> _backlog = [];
    private Batch? _lastDelivered;
    // seq continues across restarts because it starts from the wall clock
    private long _seq = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private int _tick;
    private double _battery = 60 + Random.Shared.Next(40);

    public async Task RunAsync(CancellationToken ct)
    {
        var physiology = new Physiology(_scenario, device.DeviceId.GetHashCode(), options.DeteriorationTime);
        var interval = TimeSpan.FromSeconds(options.IntervalSeconds ?? device.IntervalSeconds);
        var started = DateTimeOffset.UtcNow;
        await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next((int)interval.TotalMilliseconds)), ct);

        using var timer = new PeriodicTimer(interval);
        do
        {
            var now = DateTimeOffset.UtcNow;
            var batch = Build(physiology.Sample(now - started), now);
            if (IsOffline(now - started))
            {
                _backlog.Add(batch);
                continue;
            }

            var ok = await SendAsync(batch, ct);
            if (_backlog.Count > 0 && ok)
            {
                // reconnect: the current batch went first, so the buffered ones arrive late; and the last
                // pre-outage batch is resent because its ack was "lost"
                foreach (var old in _backlog) await SendAsync(old, ct);
                if (_lastDelivered is not null) await SendAsync(_lastDelivered, ct);
                _backlog.Clear();
            }
            if (ok) _lastDelivered = batch;
        } while (await timer.WaitForNextTickAsync(ct));
    }

    internal bool IsOffline(TimeSpan elapsed) =>
        _scenario == Scenario.Dropout && elapsed.TotalSeconds % options.DropoutCycle.TotalSeconds > options.DropoutCycle.TotalSeconds - options.DropoutLength.TotalSeconds;

    private Batch Build(Vitals v, DateTimeOffset at)
    {
        _tick++;
        _battery = Math.Max(5, _battery - 0.002);
        var readings = new List<Reading>
        {
            new(++_seq, "HeartRate", Math.Round(v.HeartRate), null, "bpm", at),
            // a third of the fleet reports saturation as a fraction, which vitals normalises
            new(++_seq, "SpO2", device.Serial.EndsWith('3') ? Math.Round(v.SpO2 / 100, 3) : Math.Round(v.SpO2, 1),
                null, device.Serial.EndsWith('3') ? "fraction" : "%", at),
            new(++_seq, "RespiratoryRate", Math.Round(v.RespiratoryRate), null, "/min", at),
            new(++_seq, "Temperature", Math.Round(v.Temperature, 1), null, "C", at)
        };
        // the cuff inflates once a minute, not every tick
        if (_tick % Math.Max(1, 60 / Math.Max(1, options.IntervalSeconds ?? device.IntervalSeconds)) == 1)
            readings.Add(new(++_seq, "BloodPressure", Math.Round(v.Systolic), Math.Round(v.Diastolic), "mmHg", at));
        if (_tick % 120 == 1)
            readings.Add(new(++_seq, "Glucose", Math.Round(v.Glucose), null, "mg/dL", at));
        return new Batch((int)_battery, readings);
    }

    private async Task<bool> SendAsync(Batch batch, CancellationToken ct)
    {
        Interlocked.Add(ref stats.Sent, batch.Readings.Count);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/ingest/readings") { Content = JsonContent.Create(batch, options: Json) };
            request.Headers.Add("X-Device-Key", device.ApiKey);
            using var response = await http.SendAsync(request, ct);
            switch (response.StatusCode)
            {
                case HttpStatusCode.Accepted:
                    Interlocked.Add(ref stats.Accepted, batch.Readings.Count);
                    return true;
                case HttpStatusCode.TooManyRequests:
                    Interlocked.Increment(ref stats.Throttled);
                    _backlog.Add(batch);
                    return false;
                default:
                    Interlocked.Increment(ref stats.Rejected);
                    return false;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            Interlocked.Increment(ref stats.Failed);
            _backlog.Add(batch);
            return false;
        }
    }
}
